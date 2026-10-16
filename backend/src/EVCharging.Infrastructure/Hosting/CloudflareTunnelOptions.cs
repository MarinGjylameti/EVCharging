using System.Diagnostics;

namespace EVCharging.Infrastructure.Hosting;

// This selects the connector's location, not OCPP admission or demonstration permissions.
public sealed class CloudflareTunnelOptions
{
    public string Mode { get; set; } = "direct";
    public string Origin { get; set; } = "";
    public string BinaryPath { get; set; } = "/usr/local/bin/cloudflared";

    public bool IsEmbedded => Mode == "embedded";

    public bool IsValid() =>
        Mode is "direct" or "external"
        || (IsEmbedded && !string.IsNullOrWhiteSpace(BinaryPath) && IsValidOrigin(Origin));

    private static bool IsValidOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var origin)
        && origin.Scheme == Uri.UriSchemeHttps
        && !origin.IsLoopback
        && origin.UserInfo.Length == 0
        && origin.AbsolutePath == "/"
        && origin.Query.Length == 0
        && origin.Fragment.Length == 0;

    public ProcessStartInfo CreateStartInfo(string configPath)
    {
        if (!IsEmbedded || !IsValid())
            throw new InvalidOperationException(
                "An embedded tunnel requires a public HTTPS API origin."
            );

        var origin = new Uri(Origin);
        var start = new ProcessStartInfo(BinaryPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (
            var argument in new[]
            {
                "tunnel",
                "--config",
                configPath,
                "--no-autoupdate",
                "--protocol",
                "http2",
                "--url",
                origin.AbsoluteUri,
                "--http-host-header",
                origin.Authority,
                "--origin-server-name",
                origin.Host,
                "--no-tls-verify=false",
                "--http2-origin=false",
                "--grace-period",
                "10s",
            }
        )
            start.ArgumentList.Add(argument);
        return start;
    }
}
