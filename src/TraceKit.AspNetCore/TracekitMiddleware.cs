using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using TraceKit.Core;

namespace TraceKit.AspNetCore;

/// <summary>
/// Middleware for automatic TraceKit instrumentation of HTTP requests
/// </summary>
public sealed class TracekitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TracekitMiddleware> _logger;
    private readonly TracekitSDK? _sdk;

    public TracekitMiddleware(
        RequestDelegate next,
        ILogger<TracekitMiddleware> logger,
        TracekitSDK? sdk)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sdk = sdk;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if SDK is not configured or is null (disabled)
        if (_sdk == null)
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;

        // Track request metrics
        var requestCounter = _sdk.Counter("http.server.requests", new Dictionary<string, string>
        {
            ["http.method"] = method,
            ["http.route"] = path
        });

        var activeGauge = _sdk.Gauge("http.server.active_requests", new Dictionary<string, string>
        {
            ["http.method"] = method
        });

        var durationHistogram = _sdk.Histogram("http.server.request.duration", new Dictionary<string, string>
        {
            ["unit"] = "ms"
        });

        activeGauge.Inc();

        // Extract and add client IP to the current span
        var clientIP = HttpUtilities.ExtractClientIP(context);
        if (!string.IsNullOrEmpty(clientIP))
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.SetTag("http.client_ip", clientIP);
            }
        }

        try
        {
            // Process the request
            await _next(context);

            // Record successful request
            requestCounter.Inc();

            // Record status code
            var statusCode = context.Response.StatusCode;
            if (statusCode >= 400)
            {
                var errorCounter = _sdk.Counter("http.server.errors", new Dictionary<string, string>
                {
                    ["http.method"] = method,
                    ["http.status_code"] = statusCode.ToString()
                });
                errorCounter.Inc();
            }
        }
        catch (Exception ex)
        {
            // Record exception
            var errorCounter = _sdk.Counter("http.server.errors", new Dictionary<string, string>
            {
                ["http.method"] = method,
                ["error.type"] = ex.GetType().Name
            });
            errorCounter.Inc();

            _logger.LogError(ex, "Unhandled exception in request {Method} {Path}", method, path);
            throw;
        }
        finally
        {
            activeGauge.Dec();
            sw.Stop();
            durationHistogram.Record(sw.ElapsedMilliseconds);
        }
    }
}
