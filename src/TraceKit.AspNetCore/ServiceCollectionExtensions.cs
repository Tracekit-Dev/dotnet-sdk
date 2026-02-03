using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TraceKit.Core;

namespace TraceKit.AspNetCore;

/// <summary>
/// Extension methods for IServiceCollection to register TraceKit services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TraceKit services to the service collection with configuration from action
    /// </summary>
    public static IServiceCollection AddTracekit(
        this IServiceCollection services,
        Action<TracekitOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        services.Configure(configureOptions);
        return AddTracekitCore(services);
    }

    /// <summary>
    /// Adds TraceKit services to the service collection with configuration from appsettings.json
    /// </summary>
    public static IServiceCollection AddTracekit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        services.Configure<TracekitOptions>(configuration.GetSection("Tracekit"));
        return AddTracekitCore(services);
    }

    /// <summary>
    /// Adds TraceKit services with both configuration sources
    /// </summary>
    public static IServiceCollection AddTracekit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TracekitOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        services.Configure<TracekitOptions>(configuration.GetSection("Tracekit"));
        services.PostConfigure(configureOptions);
        return AddTracekitCore(services);
    }

    private static IServiceCollection AddTracekitCore(IServiceCollection services)
    {
        // Register TracekitSDK as singleton
        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TracekitOptions>>().Value;

            // Validate options
            var errors = options.Validate();
            if (errors.Any())
            {
                throw new InvalidOperationException(
                    $"TraceKit configuration is invalid:\n{string.Join("\n", errors)}");
            }

            // Return null SDK if disabled
            if (!options.Enabled)
            {
                return null!;
            }

            // Build configuration
            var config = TracekitConfig.CreateBuilder()
                .WithApiKey(options.ApiKey!)
                .WithServiceName(options.ServiceName!)
                .WithEndpoint(options.Endpoint)
                .WithUseSSL(options.UseSSL)
                .WithEnvironment(options.Environment)
                .WithEnableCodeMonitoring(options.EnableCodeMonitoring)
                .WithCodeMonitoringPollInterval(options.CodeMonitoringPollIntervalSeconds)
                .Build();

            return TracekitSDK.Create(config);
        });

        // Register HttpClient for TraceKit operations
        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// Adds TraceKit instrumentation to an HttpClient
    /// </summary>
    public static IHttpClientBuilder AddTracekitInstrumentation(this IHttpClientBuilder builder)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        // HttpClient instrumentation is automatically handled by OpenTelemetry's HttpClient instrumentation
        // which is configured in TracekitSDK. No additional configuration needed here.
        return builder;
    }
}
