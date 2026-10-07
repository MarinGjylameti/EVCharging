using EVCharging.Application.Charging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Only controlled application categories; never capture EF SQL, arbitrary headers or payloads.
public sealed class OcppDiagnosticLoggerProvider(IApplicationDiagnostics diagnostics)
    : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new DiagnosticLogger(
            diagnostics,
            categoryName switch
            {
                "OcppWebSocket" or "EVCharging.Infrastructure.Charging.Ocpp.OcppGateway" => "OCPP",
                "EVCharging.Api.ApiErrors" => "HTTP",
                "EVCharging.Infrastructure.Charging.ChargerLivenessWorker" => "OCPP",
                "EVCharging.Infrastructure.Charging.StationEventWorker"
                or "EVCharging.Infrastructure.Notifications.PushWorker"
                or "EVCharging.Infrastructure.Notifications.PushDispatcher" => "Worker",
                _ => null,
            }
        );

    public void Dispose() { }

    private sealed class DiagnosticLogger(IApplicationDiagnostics diagnostics, string? source)
        : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) =>
            source is not null && logLevel >= LogLevel.Information && logLevel != LogLevel.None;

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
            var fields = (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(
                x => x.Key,
                x => x.Value
            );
            string? Field(string key) => fields?.GetValueOrDefault(key)?.ToString();
            var identity = Field("Identity");
            var message = formatter(state, null);
            if (exception is not null)
                message += " Error type: " + exception.GetType().Name + ".";
            diagnostics.Record(
                source!,
                logLevel.ToString(),
                message,
                identity,
                new(
                    TraceId: Field("TraceId"),
                    ConnectionId: Guid.TryParse(Field("ConnectionId"), out var connection)
                        ? connection
                        : null,
                    MessageId: Field("CallId"),
                    Action: Field("Action"),
                    EventName: eventId.Name,
                    ErrorType: exception?.GetType().Name ?? Field("ErrorType"),
                    ErrorCode: Field("FailureCode")
                        ?? Field("Code")
                        ?? (exception?.GetBaseException() as PostgresException)?.SqlState,
                    DurationMs: long.TryParse(Field("ElapsedMs"), out var elapsed) ? elapsed : null,
                    StackTrace: exception?.StackTrace
                )
            );
        }
    }
}
