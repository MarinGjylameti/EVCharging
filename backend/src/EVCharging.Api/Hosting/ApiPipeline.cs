namespace EVCharging.Api.Hosting;

public static class ApiPipeline
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
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
