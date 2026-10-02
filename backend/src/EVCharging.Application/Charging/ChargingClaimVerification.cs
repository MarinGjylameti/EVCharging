using EVCharging.Domain.Charging;

namespace EVCharging.Application.Charging;

// Future QR/provider verification resolves evidence here before calling the same
// claim operation. A station ID alone is accepted only by the explicit demo mode.
public interface IChargingClaimVerification
{
    bool DemoBypassEnabled { get; }
    Task<ChargingConnector> VerifyAsync(Guid stationId, CancellationToken ct);
}
