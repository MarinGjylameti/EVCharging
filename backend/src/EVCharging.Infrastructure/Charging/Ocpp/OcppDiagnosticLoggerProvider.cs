using EVCharging.Application.Charging;
using Microsoft.Extensions.Logging;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Capture only controlled OCPP messages. Exception details and frame payloads are not exposed.
public sealed class OcppDiagnosticLoggerProvider(IApplicationDiagnostics diagnostics)
    : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new DiagnosticLogger(
            diagnostics,
            categoryName is "OcppWebSocket" or "EVCharging.Infrastructure.Charging.Ocpp.OcppGateway"
        );

    public void Dispose() { }

    private sealed class DiagnosticLogger(IApplicationDiagnostics diagnostics, bool enabled)
        : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) =>
            enabled && logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;
            var identity = (state as IEnumerable<KeyValuePair<string, object?>>)
                ?.FirstOrDefault(x => x.Key == "Identity")
                .Value?.ToString();
            var message = formatter(state, null);
            if (exception is not null)
                message += " Error type: " + exception.GetType().Name + ".";
            diagnostics.Record("OCPP", logLevel.ToString(), message, identity);
        }
    }
}
