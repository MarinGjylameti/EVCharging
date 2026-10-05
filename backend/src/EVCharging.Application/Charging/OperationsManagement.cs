namespace EVCharging.Application.Charging;

public sealed record LocationInput(
    string Name,
    string City,
    string Address,
    double Latitude,
    double Longitude,
    long Version = 0
);

public sealed record LocationDto(
    Guid Id,
    string Name,
    string City,
    string Address,
    double Latitude,
    double Longitude,
    bool IsArchived,
    long Version
);

public sealed record AdministratorInput(
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? Password,
    bool IsActive,
    Guid[] LocationIds,
    Guid[] StationIds,
    long Version = 0
);

public sealed record AdministratorDetails(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    bool IsActive,
    DateTimeOffset? ArchivedAt,
    long Version,
    Guid[] LocationIds,
    Guid[] StationIds
);

public sealed record DeviceInput(
    string Identity,
    string SerialNumber,
    string? Vendor,
    string? Model,
    string Protocol,
    string[] AuthorizedTags
);

public sealed record DeviceDto(
    Guid Id,
    string Identity,
    string SerialNumber,
    string? Vendor,
    string? Model,
    string Protocol,
    bool IsEnabled,
    bool IsOnline,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastBootAt,
    int[] Connectors,
    int AuthorizedTagCount,
    long Version
);

public sealed record DeviceConnectionDetails(DeviceDto Device, string WebSocketPath);

public sealed record DeviceControlInput(long Version, bool Enabled);

public sealed record SessionFilter(
    Guid? StationId = null,
    Guid? LocationId = null,
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Until = null,
    string? Driver = null,
    int Page = 1
);

public sealed record OperationsSession(
    Guid Id,
    Guid StationId,
    string StationName,
    string StationCode,
    Guid? LocationId,
    int ConnectorNumber,
    Guid? DriverId,
    string? DriverName,
    string? DriverEmail,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    decimal? EnergyKWh,
    decimal? PowerKW,
    decimal? StateOfCharge,
    decimal UnitPriceSnapshot,
    decimal? Cost,
    string Currency,
    bool IsDemo
);

public sealed record SessionPage(
    IReadOnlyList<OperationsSession> Items,
    int Total,
    int Page,
    int PageSize
);

public sealed record OperationsSummary(
    int Stations,
    IReadOnlyDictionary<string, int> StatusCounts,
    int Locations,
    int Administrators,
    int Drivers,
    int ActiveSessions,
    int CompletedSessions,
    decimal EnergyKWh,
    decimal CalculatedCost,
    string Currency,
    DateTimeOffset UpdatedAt
);

public sealed record AuditDto(
    Guid Id,
    Guid ActorId,
    string ActorRole,
    string Action,
    Guid EntityId,
    string? ChangesJson,
    DateTimeOffset RecordedAt,
    string? ActorName = null,
    string? ResourceName = null
);

public sealed record PriceRevisionDto(
    Guid Id,
    Guid StationId,
    decimal? PreviousPrice,
    decimal PricePerKWh,
    string Currency,
    Guid ActorId,
    string Reason,
    DateTimeOffset RecordedAt
);

public sealed record DriverDirectoryEntry(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public interface IOperationsManagement
{
    Task<IReadOnlyList<LocationDto>> LocationsAsync(Guid actorId, CancellationToken ct);
    Task<LocationDto> SaveLocationAsync(
        Guid actorId,
        Guid? id,
        LocationInput input,
        CancellationToken ct
    );
    Task<LocationDto> ArchiveLocationAsync(
        Guid actorId,
        Guid id,
        long version,
        CancellationToken ct
    );
    Task<IReadOnlyList<AdministratorDetails>> AdministratorsAsync(
        Guid actorId,
        CancellationToken ct
    );
    Task<AdministratorDetails> SaveAdministratorAsync(
        Guid actorId,
        Guid? id,
        AdministratorInput input,
        CancellationToken ct
    );
    Task<AdministratorDetails> ArchiveAdministratorAsync(
        Guid actorId,
        Guid id,
        long version,
        CancellationToken ct
    );
    Task<IReadOnlyList<DriverDirectoryEntry>> DriversAsync(Guid actorId, CancellationToken ct);
    Task<DeviceDto?> DeviceAsync(Guid actorId, Guid stationId, CancellationToken ct);
    Task<DeviceConnectionDetails> ProvisionDeviceAsync(
        Guid actorId,
        Guid stationId,
        DeviceInput input,
        CancellationToken ct
    );
    Task<DeviceDto> ControlDeviceAsync(
        Guid actorId,
        Guid stationId,
        DeviceControlInput input,
        CancellationToken ct
    );
    Task<OperationsSummary> SummaryAsync(Guid actorId, CancellationToken ct);
    Task<SessionPage> SessionsAsync(Guid actorId, SessionFilter filter, CancellationToken ct);
    Task<IReadOnlyList<AuditDto>> AuditAsync(Guid actorId, Guid? stationId, CancellationToken ct);
    Task<IReadOnlyList<PriceRevisionDto>> PricesAsync(
        Guid actorId,
        Guid? stationId,
        CancellationToken ct
    );
}
