namespace EVCharging.Application.Charging;

public interface IOcppGateway
{
    Task<Guid?> AuthenticateAsync(
        string identity,
        string username,
        string password,
        CancellationToken ct
    );
    Task<Guid> OpenAsync(Guid chargePointId, CancellationToken ct);
    Task<string> ProcessAsync(
        Guid chargePointId,
        Guid connectionId,
        string frame,
        CancellationToken ct
    );
    Task CloseAsync(Guid chargePointId, Guid connectionId, int? closeCode, CancellationToken ct);
}

public sealed record StationChanged(
    Guid StationId,
    long Version,
    string Status,
    Guid? RecipientAccountId = null
);

public interface IStationEventStream
{
    IAsyncEnumerable<StationChanged> Subscribe(Guid accountId, CancellationToken ct);
}
