using System.Text.Json.Serialization;

namespace EVCharging.Api.Hosting;

public static class ApiHttpConfiguration
{
    public static WebApplicationBuilder ConfigureApiHttp(
        this WebApplicationBuilder builder,
        string[] allowedOrigins
    )
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32_768);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        );
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
            {
                if (allowedOrigins.Length > 0)
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            })
        );

        return builder;
    }
}
