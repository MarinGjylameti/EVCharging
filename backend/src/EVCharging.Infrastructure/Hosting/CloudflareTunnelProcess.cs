using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EVCharging.Infrastructure.Hosting;

public class CloudflareTunnelProcess(ILogger<CloudflareTunnelProcess> logger)
{
    // Virtual at the OS boundary so lifecycle tests never open a public tunnel.
    public virtual async Task<int> RunAsync(
        CloudflareTunnelOptions options,
        CancellationToken cancellationToken
    )
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"evcharging-tunnel-{Guid.NewGuid():N}.yml"
        );
        try
        {
            // Ignore any inherited account configuration: these trials use a Quick Tunnel.
            await File.WriteAllTextAsync(configPath, "{}", cancellationToken);
            using var process = new Process { StartInfo = CreateStartInfo(options, configPath) };
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            using var stop = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            try
            {
                var output = RelayAsync(process.StandardOutput);
                var error = RelayAsync(process.StandardError);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(output, error);
                cancellationToken.ThrowIfCancellationRequested();
                return process.ExitCode;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    protected virtual ProcessStartInfo CreateStartInfo(
        CloudflareTunnelOptions options,
        string configPath
    ) => options.CreateStartInfo(configPath);

    private async Task RelayAsync(StreamReader stream)
    {
        while (await stream.ReadLineAsync() is { } line)
        {
            logger.LogInformation("cloudflared: {Line}", line);
            var match = Regex.Match(
                line,
                @"https://[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com(?=[\s/|""\x1b]|$)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)
            );
            if (match.Success)
                logger.LogInformation(
                    "OCPP embedded Quick Tunnel: {TunnelUrl}. Use its /ws/cs/<identity> path; this URL changes when the connector restarts.",
                    match.Value
                );
        }
    }
}
