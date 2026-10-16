using System.Diagnostics;
using EVCharging.Infrastructure;
using EVCharging.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class CloudflareTunnelTests
{
    private const string Origin = "https://vcharging-api-demo.onrender.com";

    [Theory]
    [InlineData("direct")]
    [InlineData("external")]
    public async Task Non_embedded_modes_never_start_a_connector(string mode)
    {
        var lifetime = new TestLifetime();
        lifetime.Started.Cancel();
        var connector = new FakeConnector();
        using var worker = CreateWorker(mode, connector, lifetime);
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        Assert.Equal(0, connector.Attempts);
        Assert.False(lifetime.Stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Embedded_waits_for_api_start_and_cancels_its_child_when_api_stops()
    {
        var connector = new FakeConnector();
        var lifetime = new TestLifetime();
        using var worker = CreateWorker("embedded", connector, lifetime);
        await worker.StartAsync(CancellationToken.None);
        Assert.Equal(0, connector.Attempts);
        lifetime.Started.Cancel();
        await connector.Running.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, connector.Attempts);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(connector.LastToken.IsCancellationRequested);
        Assert.False(lifetime.Stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Shutdown_before_application_started_never_runs_a_connector()
    {
        var connector = new FakeConnector();
        using var worker = CreateWorker("embedded", connector, new TestLifetime());
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, connector.Attempts);
    }

    [Fact]
    public async Task Api_health_stays_available_after_embedded_connector_failure_with_test_mode_off()
    {
        var connector = new FakeConnector { FailFirst = true, ThrowFirst = true };
        using var original = new ApiFactory(testMode: false);
        using var factory = original.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Configure<CloudflareTunnelOptions>(options =>
                {
                    options.Mode = "embedded";
                    options.Origin = Origin;
                });
                services.RemoveAll<CloudflareTunnelProcess>();
                services.AddSingleton<CloudflareTunnelProcess>(connector);
            })
        );
        using var client = factory.CreateClient();
        await connector.FirstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var health = await client.GetAsync("/health/ready");
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("ready", await health.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connector_failure_or_exit_retries_without_stopping_the_api(bool throws)
    {
        var connector = new FakeConnector { FailFirst = true, ThrowFirst = throws };
        var lifetime = new TestLifetime();
        lifetime.Started.Cancel();
        using var worker = CreateWorker("embedded", connector, lifetime);
        await worker.StartAsync(CancellationToken.None);
        await connector.Running.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, connector.Attempts);
        Assert.False(lifetime.Stopped.IsCancellationRequested);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(connector.LastToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("direct", null, true)]
    [InlineData("external", null, true)]
    [InlineData("embedded", Origin, true)]
    [InlineData("embedded", null, false)]
    [InlineData("invalid", Origin, false)]
    [InlineData("embedded", "http://api.example.test", false)]
    [InlineData("embedded", "https://127.0.0.1", false)]
    [InlineData("embedded", "https://localhost", false)]
    [InlineData("embedded", "https://user:password@api.example.test", false)]
    [InlineData("embedded", "https://api.example.test/ws/cs/", false)]
    [InlineData("embedded", "https://api.example.test/?token=secret", false)]
    [InlineData("embedded", "https://api.example.test/#fragment", false)]
    public void Route_configuration_is_explicit_and_rejects_unsafe_or_missing_origins(
        string? mode,
        string? origin,
        bool valid
    )
    {
        using var services = BuildServices(mode, origin);
        var options = services.GetRequiredService<IOptions<CloudflareTunnelOptions>>();
        if (valid)
        {
            Assert.Equal(mode ?? "direct", options.Value.Mode);
            Assert.Equal(mode == "embedded", options.Value.IsEmbedded);
        }
        else
            Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Embedded_origin_can_use_render_metadata_and_does_not_depend_on_test_mode(
        bool testMode
    )
    {
        using var services = BuildServices("embedded", null, Origin, testMode);
        var options = services.GetRequiredService<IOptions<CloudflareTunnelOptions>>().Value;
        Assert.Equal(Origin, options.Origin);
        Assert.True(options.IsEmbedded);
        Assert.Equal(
            testMode,
            services
                .GetRequiredService<IOptions<EVCharging.Application.Charging.RuntimeFeatures>>()
                .Value.TestMode
        );
    }

    [Fact]
    public void Explicit_origin_wins_and_matches_the_external_connector_host_sni_and_tls()
    {
        using var services = BuildServices("embedded", Origin, "https://different.example.test");
        var options = services.GetRequiredService<IOptions<CloudflareTunnelOptions>>().Value;
        var start = options.CreateStartInfo("config path with spaces.yml");
        Assert.Equal("/usr/local/bin/cloudflared", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.RedirectStandardError);
        Assert.True(start.RedirectStandardOutput);
        Assert.Contains("config path with spaces.yml", start.ArgumentList);
        Assert.Contains("--no-autoupdate", start.ArgumentList);
        Assert.Contains("--no-tls-verify=false", start.ArgumentList);
        Assert.Contains("--http2-origin=false", start.ArgumentList);
        var args = start.ArgumentList.ToList();
        Assert.Equal("http2", args[args.IndexOf("--protocol") + 1]);
        Assert.Equal(Origin + "/", args[args.IndexOf("--url") + 1]);
        Assert.Equal(
            "vcharging-api-demo.onrender.com",
            args[args.IndexOf("--http-host-header") + 1]
        );
        Assert.Equal(
            "vcharging-api-demo.onrender.com",
            args[args.IndexOf("--origin-server-name") + 1]
        );
        Assert.DoesNotContain("run", args);
        Assert.DoesNotContain("--token", args);
    }

    [Fact]
    public async Task Real_child_is_terminated_and_temporary_configuration_removed_on_cancellation()
    {
        var output = new ChildOutput();
        var process = new LocalChild(output);
        using var cancellation = new CancellationTokenSource();
        var run = process.RunAsync(new CloudflareTunnelOptions(), cancellation.Token);
        try
        {
            var id = await output.Id.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var child = Process.GetProcessById(id);
            Assert.False(child.HasExited);
            Assert.Contains(
                "https://example-connector-test.trycloudflare.com",
                await output.Url.Task.WaitAsync(TimeSpan.FromSeconds(5))
            );
            Assert.Equal("{}", await File.ReadAllTextAsync(process.ConfigPath!));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                run.WaitAsync(TimeSpan.FromSeconds(5))
            );
            Assert.True(child.HasExited);
            Assert.False(File.Exists(process.ConfigPath));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) { }
        }
    }

    private static ServiceProvider BuildServices(
        string? mode,
        string? origin,
        string? renderOrigin = null,
        bool testMode = false
    )
    {
        var values = new Dictionary<string, string?>
        {
            ["Features:TestMode"] = testMode.ToString(),
        };
        if (mode is not null)
            values["Tunnel:Mode"] = mode;
        if (origin is not null)
            values["Tunnel:Origin"] = origin;
        if (renderOrigin is not null)
            values["RENDER_EXTERNAL_URL"] = renderOrigin;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()
        );
        services.AddLogging();
        services.AddChargingInfrastructure(null);
        return services.BuildServiceProvider();
    }

    private static CloudflareTunnelWorker CreateWorker(
        string mode,
        FakeConnector connector,
        TestLifetime lifetime
    ) =>
        new(
            Options.Create(new CloudflareTunnelOptions { Mode = mode, Origin = Origin }),
            connector,
            lifetime,
            NullLogger<CloudflareTunnelWorker>.Instance
        );

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationTokenSource Started { get; } = new();
        public CancellationTokenSource Stopped { get; } = new();
        public CancellationToken ApplicationStarted => Started.Token;
        public CancellationToken ApplicationStopping => Stopped.Token;
        public CancellationToken ApplicationStopped => Stopped.Token;

        public void StopApplication() => Stopped.Cancel();
    }

    private sealed class FakeConnector()
        : CloudflareTunnelProcess(NullLogger<CloudflareTunnelProcess>.Instance)
    {
        public int Attempts { get; private set; }
        public bool FailFirst { get; init; }
        public bool ThrowFirst { get; init; }
        public CancellationToken LastToken { get; private set; }
        public TaskCompletionSource Running { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstAttempt { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<int> RunAsync(
            CloudflareTunnelOptions options,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            FirstAttempt.TrySetResult();
            LastToken = cancellationToken;
            if (FailFirst && Attempts == 1)
            {
                if (ThrowFirst)
                    throw new InvalidOperationException("Simulated missing executable");
                return 19;
            }
            Running.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class ChildOutput : ILogger<CloudflareTunnelProcess>
    {
        public TaskCompletionSource<int> Id { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Url { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var line = formatter(state, exception);
            if (line.StartsWith("cloudflared: child="))
                Id.TrySetResult(int.Parse(line["cloudflared: child=".Length..]));
            if (line.StartsWith("OCPP embedded Quick Tunnel:"))
                Url.TrySetResult(line);
        }
    }

    // Launch an actual local child without cloudflared, a network request, or a container engine.
    private sealed class LocalChild(ChildOutput output) : CloudflareTunnelProcess(output)
    {
        public string? ConfigPath { get; private set; }

        protected override ProcessStartInfo CreateStartInfo(
            CloudflareTunnelOptions options,
            string configPath
        )
        {
            ConfigPath = configPath;
            var start = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (OperatingSystem.IsWindows())
            {
                start.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                );
                foreach (
                    var argument in new[]
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        "Write-Output ('child=' + $PID); [Console]::Error.WriteLine('| https://example-connector-test.trycloudflare.com |'); Start-Sleep -Seconds 60",
                    }
                )
                    start.ArgumentList.Add(argument);
            }
            else
            {
                start.FileName = "/bin/sh";
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(
                    "echo child=$$; echo '| https://example-connector-test.trycloudflare.com |' >&2; exec sleep 60"
                );
            }
            return start;
        }
    }
}
