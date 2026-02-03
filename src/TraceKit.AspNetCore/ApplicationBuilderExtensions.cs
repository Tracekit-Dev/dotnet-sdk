using Microsoft.AspNetCore.Builder;

namespace TraceKit.AspNetCore;

/// <summary>
/// Extension methods for IApplicationBuilder to use TraceKit middleware
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds TraceKit middleware to the application pipeline.
    /// This should be called early in the pipeline to ensure all requests are tracked.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseTracekit(this IApplicationBuilder app)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<TracekitMiddleware>();
    }
}
