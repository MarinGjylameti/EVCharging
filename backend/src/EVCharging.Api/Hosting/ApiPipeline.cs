using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;

namespace EVCharging.Api.Hosting;

public static class ApiPipeline
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.Services.GetRequiredService<ILoggerFactory>()
            .AddProvider(
                new OcppDiagnosticLoggerProvider(
                    app.Services.GetRequiredService<IApplicationDiagnostics>()
                )
            );
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.UseMiddleware<ApplicationRequestDiagnostics>();
        app.UseMiddleware<ApiErrors>();
        app.Use(
            async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next(context);
            }
        );
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
