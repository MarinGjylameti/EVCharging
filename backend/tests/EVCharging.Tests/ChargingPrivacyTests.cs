using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    [Fact]
    public async Task CancelledWaitingDoesNotCaptureAnotherDriversChargeAndUnplugReleasesOnlyItsOwner()
    {
        using var visitor = await DriverAsync();
        using var owner = await DriverAsync("actual-owner@test.com");
        var ownerUser = await CapacityAsync(owner, 77);
        using var socket = await ConnectAsync();
        var station = await MapStationAsync(visitor, "SIM-001");
        await ClaimAsync(visitor);
        using var cancelled = await visitor.PostAsync(
            $"/api/v1/driver/stations/{station.Id}/cancel",
            null,
            timeout.Token
        );
        cancelled.EnsureSuccessStatusCode();
        await ClaimAsync(owner);
        var transactionId = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [
                new
                {
                    value = "1000",
                    unit = "Wh",
                    measurand = "Energy.Active.Import.Register",
                },
                new
                {
                    value = "22",
                    unit = "kW",
                    measurand = "Power.Active.Import",
                },
                new
                {
                    value = "45",
                    unit = "Percent",
                    measurand = "SoC",
                },
            ],
            transactionId
        );
        for (var visit = 0; visit < 2; visit++)
        {
            var publicView = await ChargingViewAsync(visitor);
            Assert.True(publicView.InUse);
            Assert.Null(publicView.Session);
            Assert.Null(publicView.Diagnostics);
            Assert.Null(publicView.ConnectionRequest);
            Assert.NotNull(publicView.Station.AvailabilityForecast);
        }
        using var adoption = await visitor.PostAsync(
            $"/api/v1/driver/stations/{station.Id}/connect",
            null,
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.Conflict, adoption.StatusCode);
        var privateView = await ChargingViewAsync(owner);
        Assert.False(privateView.InUse);
        Assert.Equal(45, privateView.Session!.StateOfCharge);
        Assert.Equal(.35m, privateView.Session.Cost);
        Assert.NotNull(privateView.Diagnostics);

        var stop = new
        {
            transactionId,
            meterStop = 1200,
            timestamp = DateTimeOffset.UtcNow,
            reason = "EVDisconnected",
        };
        Assert.Equal(3, (await CallAsync(socket, "StopTransaction", stop))[0].GetInt32());
        Assert.Equal(3, (await CallAsync(socket, "StopTransaction", stop))[0].GetInt32());
        await StatusAsync(socket, "Available");
        var completed = (await ChargingViewAsync(owner)).Session!;
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("EVDisconnected", completed.StopReason);
        Assert.Equal(1.2m, completed.EnergyKWh);
        Assert.Equal(.42m, completed.Cost);
        Assert.Null((await ChargingViewAsync(visitor)).Session);
        Assert.Empty(
            (
                await visitor.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        Assert.Single(
            (
                await owner.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var claims = await db.ChargingConnectionRequests.ToListAsync();
            Assert.Single(claims, x => x.State == "Cancelled");
            Assert.Equal(ownerUser.Id, Assert.Single(claims, x => x.State == "Completed").DriverId);
            Assert.DoesNotContain(claims, x => x.State is "Waiting" or "Linked");
            var notification = Assert.Single(
                await db
                    .Notifications.Where(x => x.Kind == NotificationKind.ChargingCompleted)
                    .ToListAsync()
            );
            Assert.Equal(ownerUser.Id, notification.UserId);
            Assert.All(
                await db
                    .RealtimeOutbox.Where(x =>
                        x.Audience == "Driver" && x.EventType.StartsWith("Session")
                    )
                    .ToListAsync(),
                x => Assert.Equal(ownerUser.Id, x.RecipientAccountId)
            );
        }
        // A confirmed physical stop allows a different account to prepare the next charge.
        await ClaimAsync(visitor);
    }

    [Fact]
    public async Task PreviousPersonalSummaryIsHiddenDuringANewUnownedCharge()
    {
        using var visitor = await DriverAsync();
        using var second = await DriverAsync("public-only@test.com");
        using var socket = await ConnectAsync();
        await ClaimAsync(visitor);
        var previous = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = previous,
                meterStop = 1000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.NotNull((await ChargingViewAsync(visitor)).Session);
        await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        foreach (var account in new[] { visitor, second })
        {
            var view = await ChargingViewAsync(account);
            Assert.True(view.InUse);
            Assert.Null(view.Session);
            Assert.Null(view.Diagnostics);
            using var denied = await account.PostAsync(
                $"/api/v1/driver/stations/{view.Station.Id}/connect",
                null,
                timeout.Token
            );
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        }
        Assert.Single(
            (
                await visitor.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Single(await db.ChargingConnectionRequests.ToListAsync());
        Assert.Equal(2, await db.ChargingSessions.CountAsync());
    }

    [Fact]
    public async Task NetworkLossWarnsOnlyTheOwnerAndKeepsChargeOpenUntilConfirmedUnplug()
    {
        using var owner = await DriverAsync();
        using var observer = await DriverAsync("network-observer@test.com");
        var ownerUser = await CapacityAsync(owner, 60);
        using var socket = await ConnectAsync();
        await ClaimAsync(owner);
        var transactionId = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "network lost", timeout.Token);
        var lost = (await ChargingViewAsync(owner)).Session!;
        Assert.Null(lost.StoppedAt);
        Assert.Equal("ConnectionLost", lost.Status);
        Assert.Null(lost.PowerKW);
        Assert.Null((await ChargingViewAsync(observer)).Session);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal("Linked", (await db.ChargingConnectionRequests.SingleAsync()).State);
            Assert.Null((await db.ChargingSessions.SingleAsync()).EndedAt);
            var warning = Assert.Single(
                await db
                    .Notifications.Where(x => x.Kind == NotificationKind.UnexpectedDisconnect)
                    .ToListAsync()
            );
            Assert.Equal(NotificationKind.UnexpectedDisconnect, warning.Kind);
            Assert.Equal(ownerUser.Id, warning.UserId);
            var text = NotificationTemplates.Get(warning.Kind, AppLanguage.En);
            Assert.Contains("reconnection", text.Body);
        }
        using var reconnected = await ConnectAsync();
        await CallAsync(
            reconnected,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 500,
                timestamp = DateTimeOffset.UtcNow,
                reason = "EVDisconnected",
            }
        );
        Assert.NotNull((await ChargingViewAsync(owner)).Session!.StoppedAt);
        using var finalScope = factory.Services.CreateScope();
        var final = finalScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Equal("Completed", (await final.ChargingConnectionRequests.SingleAsync()).State);
        Assert.Equal(3, await final.Notifications.CountAsync());
        Assert.All(
            await final.Notifications.ToListAsync(),
            x => Assert.Equal(ownerUser.Id, x.UserId)
        );
    }
}
