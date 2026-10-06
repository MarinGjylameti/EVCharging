using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using Microsoft.Extensions.Logging;

namespace EVCharging.Tests;

public sealed class ApplicationDiagnosticsTests
{
    [Fact]
    public void BufferEvictsOldEventsAndSanitizesMessagesWhilePreservingOrderAndFiltering()
    {
        var logs = new ApplicationDiagnostics(TimeProvider.System);
        for (var n = 0; n < 50; n++)
            logs.Record("OCPP", "Information", "old", "DROPPED");
        for (var n = 0; n < 1000; n++)
            logs.Record("OCPP", "Information", "entry\n" + n, "CP-001");
        Assert.Empty(logs.Read(identity: "DROPPED").Entries);
        var page = logs.Read("OCPP", "cp-");
        Assert.Equal(1000, page.Capacity);
        Assert.Equal(200, page.Entries.Count);
        Assert.Equal(1050, page.Entries[0].Sequence);
        Assert.Equal("entry 999", page.Entries[0].Message);
        Assert.Empty(logs.Read("HTTP").Entries);
    }

    [Fact]
    public void OcppProviderCapturesControlledMessagesAndErrorTypeWithoutExceptionSecrets()
    {
        var logs = new ApplicationDiagnostics(TimeProvider.System);
        using var provider = new OcppDiagnosticLoggerProvider(logs);
        provider
            .CreateLogger("Microsoft.EntityFrameworkCore.Database.Command")
            .LogError("private SQL");
        provider
            .CreateLogger("OcppWebSocket")
            .LogError(
                new Exception("private token"),
                "OCPP failure for {Identity}.",
                "NKR2023072805"
            );
        var entry = Assert.Single(logs.Read().Entries);
        Assert.Equal("NKR2023072805", entry.Identity);
        Assert.Contains("Error type: Exception", entry.Message);
        Assert.DoesNotContain("private token", entry.Message);
        Assert.DoesNotContain("private SQL", entry.Message);
    }
}
