namespace EVCharging.Api.Ocpp;

// Compatibility routes only admit WebSocket upgrades; ordinary HTTP stays 404.
internal sealed record OcppEndpointRoute(bool Compatibility);
