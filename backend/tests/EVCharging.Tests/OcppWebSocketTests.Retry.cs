using System.Net.WebSockets;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    [Fact]
    public async Task CommittedStartReplaysAcrossReconnectAfterMoreThanOneDay()
    {
        using var first = await ConnectAsync();
        var timestamp = DateTimeOffset.UtcNow;
        var payload = new
        {
            connectorId = 1,
            idTag = "TAG-001",
            meterStart = 0,
            timestamp,
        };
        var original = await CallAsync(first, "StartTransaction", payload, "retained-start");
        Assert.Equal(3, original[0].GetInt32());
        var transactionId = original[2].GetProperty("transactionId").GetInt32();
        await AgeRetryMessageAsync("retained-start");
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", timeout.Token);

        using var second = await ConnectAsync();
        var replay = await CallAsync(second, "StartTransaction", payload, "retained-start");
        Assert.Equal(original.GetRawText(), replay.GetRawText());
        var changed = await CallAsync(
            second,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 1,
                timestamp,
            },
            "retained-start"
        );
        Assert.Equal(4, changed[0].GetInt32());
        Assert.Equal("OccurrenceConstraintViolation", changed[2].GetString());
        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", timeout.Token);

        using var third = await ConnectAsync();
        Assert.Equal(3, (
                await CallAsync(
                    third,
                    "StopTransaction",
                    new
                    {
                        transactionId,
                        meterStop = 1000,
                        timestamp = DateTimeOffset.UtcNow,
                    }
                )
            )[0].GetInt32());
        // Even after completion, the retained original must not create another charge.
        var completedReplay = await CallAsync(third, "StartTransaction", payload, "retained-start");
        Assert.Equal(original.GetRawText(), completedReplay.GetRawText());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Single(await db.OcppChargingTransactions.ToListAsync());
        Assert.NotNull(Assert.Single(await db.ChargingSessions.ToListAsync()).EndedAt);
        await third.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task OldRejectedStartCanBeRetriedAfterTheBlockingTransactionStops()
    {
        using var first = await ConnectAsync();
        var payload = new
        {
            connectorId = 1,
            idTag = "TAG-001",
            meterStart = 0,
            timestamp = DateTimeOffset.UtcNow,
        };
        var started = await CallAsync(first, "StartTransaction", payload, "blocking-start");
        var transactionId = started[2].GetProperty("transactionId").GetInt32();
        Assert.Equal(
            4,
            (await CallAsync(first, "StartTransaction", payload, "rejected-start"))[0].GetInt32()
        );
        await AgeRetryMessageAsync("rejected-start");
        Assert.Equal(3, (
                await CallAsync(
                    first,
                    "StopTransaction",
                    new
                    {
                        transactionId,
                        meterStop = 0,
                        timestamp = DateTimeOffset.UtcNow,
                    }
                )
            )[0].GetInt32());
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", timeout.Token);

        using var second = await ConnectAsync();
        var retry = await CallAsync(second, "StartTransaction", payload, "rejected-start");
        Assert.Equal(3, retry[0].GetInt32());
        Assert.NotEqual(transactionId, retry[2].GetProperty("transactionId").GetInt32());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Equal(2, await db.OcppChargingTransactions.CountAsync());
        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    private async Task AgeRetryMessageAsync(string callId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        (await db.OcppInboundMessages.SingleAsync(x => x.CallId == callId)).ReceivedAt =
            DateTimeOffset.UtcNow.AddDays(-2);
        await db.SaveChangesAsync();
    }
}
