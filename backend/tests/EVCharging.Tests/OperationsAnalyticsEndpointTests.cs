using System.Net;
using System.Net.Http.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    [Fact]
    public async Task AnalyticsEndpointValidatesCalendarFiltersAndPreservesAuthorization()
    {
        var (super, _) = await UserAsync("analytics-endpoint@test.invalid", UserRole.Superadmin);
        var (driver, _) = await UserAsync("analytics-driver@test.invalid", UserRole.Driver);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/analytics")).StatusCode
        );
        foreach (
            var query in new[]
            {
                "period=Yearly",
                "period=Custom",
                "period=Custom&from=2026-10-07&until=2026-10-06",
                "timeZone=Invalid%2FZone",
                "date=broken",
            }
        )
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await super.GetAsync("/api/v1/admin/analytics?" + query)).StatusCode
            );
        var result = await super.GetFromJsonAsync<OperationsAnalytics>(
            "/api/v1/admin/analytics?period=Monthly&timeZone=Europe%2FTirane"
        );
        Assert.Equal("Europe/Tirane", result!.Period.TimeZone);
        Assert.Equal("Daily", result.Period.Granularity);
    }
}
