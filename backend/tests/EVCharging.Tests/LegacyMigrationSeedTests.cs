using System.Text.RegularExpressions;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using EVCharging.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EVCharging.Tests;

public sealed class LegacyMigrationSeedTests
{
    [Fact]
    public void FixtureInsertsUseOnlyColumnsAvailableBeforeTheBusinessSessionUpgrade()
    {
        var legacy = new DriverChargingConnections().TargetModel;
        FormattableString[] inserts =
        [
            LegacyMigrationSeed.Account(new Account()),
            LegacyMigrationSeed.Station(new ChargingStation()),
            LegacyMigrationSeed.Connection(new OcppConnection()),
            LegacyMigrationSeed.Message(new OcppInboundMessage()),
            LegacyMigrationSeed.Claim(new ChargingConnectionRequest()),
            LegacyMigrationSeed.History(new ChargingHistory()),
        ];
        foreach (var insert in inserts)
        {
            var match = Regex.Match(insert.Format, @"INSERT INTO (\w+) \(([^)]+)\)");
            Assert.True(match.Success);
            var table = match.Groups[1].Value;
            var columns = match.Groups[2].Value.Split(',').Select(x => x.Trim()).ToArray();
            var entity = Assert.Single(legacy.GetEntityTypes(), x => x.GetTableName() == table);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var properties = entity.GetProperties().ToArray();
            Assert.All(
                columns,
                column =>
                    Assert.Contains(properties, property => property.GetColumnName(store) == column)
            );
            Assert.All(
                properties.Where(x =>
                    !x.IsNullable
                    && x.ValueGenerated == ValueGenerated.Never
                    && x.FindAnnotation(RelationalAnnotationNames.DefaultValue) is null
                ),
                property => Assert.Contains(property.GetColumnName(store), columns)
            );
        }
        var stations = Assert.Single(
            legacy.GetEntityTypes(),
            x => x.GetTableName() == "charging_stations"
        );
        Assert.Null(stations.FindProperty(nameof(ChargingStation.ForecastValidUntil)));
        Assert.Null(stations.FindProperty(nameof(ChargingStation.LocationId)));
        var messages = Assert.Single(
            legacy.GetEntityTypes(),
            x => x.GetTableName() == "ocpp_inbound_messages"
        );
        Assert.Null(messages.FindProperty(nameof(OcppInboundMessage.CommandVersion)));
        var claims = Assert.Single(
            legacy.GetEntityTypes(),
            x => x.GetTableName() == "charging_connection_requests"
        );
        Assert.Null(claims.FindProperty(nameof(ChargingConnectionRequest.IsAutomatic)));
    }
}
