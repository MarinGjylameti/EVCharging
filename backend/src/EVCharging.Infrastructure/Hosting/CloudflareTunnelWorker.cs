using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Hosting;

public sealed class CloudflareTunnelWorker(
    IOptions<CloudflareTunnelOptions> options,
    CloudflareTunnelProcess connector,
    IHostApplicationLifetime lifetime,
    ILogger<CloudflareTunnelWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation(
            "OCPP route mode: {Mode}. Direct /ws/cs/<identity> access remains available; TestMode is independent.",
            settings.Mode
        );
        if (!settings.IsEmbedded)
            return;

        // Startup commands/migrations do not run the connector, nor does a host that failed to start.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => ready.TrySetResult());
        try
        {
            await ready.Task.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogInformation(
                        "Starting embedded OCPP Quick Tunnel to {Origin}.",
                        settings.Origin
                    );
                    var exitCode = await connector.RunAsync(settings, stoppingToken);
                    logger.LogWarning(
                        "Embedded OCPP connector exited with code {ExitCode}. API stays online; retry in 5 seconds will create a NEW tunnel URL.",
                        exitCode
                    );
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    logger.LogWarning(
                        error,
                        "Embedded OCPP connector failed. API stays online; retry in 5 seconds may create a NEW tunnel URL."
                    );
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
