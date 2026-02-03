using TraceKit.Core;
using DotNetEnv;

// Load .env file if it exists
Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Load configuration from environment variables
var enabled = Environment.GetEnvironmentVariable("TRACEKIT_ENABLED") != "false";
var apiKey = Environment.GetEnvironmentVariable("TRACEKIT_API_KEY") ?? "test_key";
var endpoint = Environment.GetEnvironmentVariable("TRACEKIT_ENDPOINT") ?? "http://localhost:8081";
var serviceName = Environment.GetEnvironmentVariable("TRACEKIT_SERVICE_NAME") ?? "dotnet-sdk";
var codeMonitoringEnabled = Environment.GetEnvironmentVariable("TRACEKIT_CODE_MONITORING_ENABLED") != "false";

// Initialize TraceKit SDK
var config = TracekitConfig.CreateBuilder()
    .WithApiKey(apiKey)
    .WithServiceName(serviceName)
    .WithEndpoint(endpoint)
    .WithEnvironment("development")
    .WithEnableCodeMonitoring(codeMonitoringEnabled)
    .Build();

var sdk = TracekitSDK.Create(config);

// Store SDK in DI container
builder.Services.AddSingleton(sdk);
builder.Services.AddHttpClient();

var app = builder.Build();

// Service URLs for cross-service communication
const string GO_SERVICE_URL = "http://localhost:8082";
const string NODE_SERVICE_URL = "http://localhost:8084";
const string PYTHON_SERVICE_URL = "http://localhost:5001";
const string PHP_SERVICE_URL = "http://localhost:8086";
const string LARAVEL_SERVICE_URL = "http://localhost:8083";
const string JAVA_SERVICE_URL = "http://localhost:8080";

// Initialize metrics
var requestCounter = sdk.Counter("http.requests.total", new() { ["service"] = serviceName });
var activeRequestsGauge = sdk.Gauge("http.requests.active", new() { ["service"] = serviceName });
var requestDurationHistogram = sdk.Histogram("http.request.duration", new() { ["unit"] = "ms" });
var errorCounter = sdk.Counter("http.errors.total", new() { ["service"] = serviceName });

// Metrics middleware
app.Use(async (context, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    activeRequestsGauge.Inc();

    try
    {
        await next();
        requestCounter.Inc();
    }
    catch
    {
        errorCounter.Inc();
        throw;
    }
    finally
    {
        activeRequestsGauge.Dec();
        requestDurationHistogram.Record(sw.ElapsedMilliseconds);
    }
});

// Root endpoint
app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        service = serviceName,
        message = "TraceKit .NET Test Application",
        features = new[] { "tracing", "snapshots", "metrics" },
        endpoints = new Dictionary<string, string>
        {
            ["GET /"] = "This endpoint",
            ["GET /health"] = "Health check",
            ["GET /test"] = "Code monitoring test",
            ["GET /error-test"] = "Error test",
            ["GET /checkout"] = "Checkout simulation",
            ["GET /api/data"] = "Data endpoint (called by other services)",
            ["GET /api/call-go"] = "Call Go service",
            ["GET /api/call-node"] = "Call Node service",
            ["GET /api/call-python"] = "Call Python service",
            ["GET /api/call-php"] = "Call PHP service",
            ["GET /api/call-laravel"] = "Call Laravel service",
            ["GET /api/call-java"] = "Call Java service",
            ["GET /api/call-all"] = "Call all services"
        }
    });
});

// Health check
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        service = serviceName,
        timestamp = DateTime.UtcNow.ToString("O")
    });
});

// Test endpoint with code monitoring
app.MapGet("/test", () =>
{
    sdk.CaptureSnapshot("test-route-entry", new Dictionary<string, object>
    {
        ["route"] = "test",
        ["method"] = "GET",
        ["timestamp"] = DateTime.UtcNow
    });

    // Simulate some processing
    var userId = Random.Shared.Next(1, 1000);
    var cartTotal = Random.Shared.Next(10, 500);

    sdk.CaptureSnapshot("test-processing", new Dictionary<string, object>
    {
        ["userId"] = userId,
        ["cartTotal"] = cartTotal,
        ["processingStep"] = "validation"
    });

    sdk.CaptureSnapshot("test-complete", new Dictionary<string, object>
    {
        ["userId"] = userId,
        ["totalProcessed"] = cartTotal,
        ["status"] = "success"
    });

    return Results.Ok(new
    {
        message = "Code monitoring test completed!",
        data = new { userId, cartTotal }
    });
});

// Error test endpoint
app.MapGet("/error-test", () =>
{
    sdk.CaptureSnapshot("error-test-start", new Dictionary<string, object>
    {
        ["route"] = "error-test",
        ["intent"] = "trigger_exception"
    });

    throw new Exception("This is a test exception for code monitoring!");
});

// Checkout simulation
app.MapGet("/checkout", () =>
{
    var userId = int.Parse(app.Configuration["user_id"] ?? "123");
    var amount = double.Parse(app.Configuration["amount"] ?? "99.99");

    sdk.CaptureSnapshot("checkout-start", new Dictionary<string, object>
    {
        ["userId"] = userId,
        ["amount"] = amount
    });

    var result = new
    {
        paymentId = $"pay_{Guid.NewGuid():N}",
        amount,
        status = "completed",
        timestamp = DateTime.UtcNow.ToString("O")
    };

    sdk.CaptureSnapshot("checkout-complete", new Dictionary<string, object>
    {
        ["userId"] = userId,
        ["amount"] = amount,
        ["paymentId"] = result.paymentId,
        ["status"] = result.status
    });

    return Results.Ok(result);
});

// Data endpoint for other services to call
app.MapGet("/api/data", () =>
{
    Thread.Sleep(Random.Shared.Next(10, 50)); // Simulate processing

    return Results.Ok(new
    {
        service = serviceName,
        timestamp = DateTime.UtcNow.ToString("O"),
        data = new
        {
            framework = ".NET",
            version = Environment.Version.ToString(),
            randomValue = Random.Shared.Next(1, 100)
        }
    });
});

// Cross-service call endpoints
app.MapGet("/api/call-go", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{GO_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "go-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "go-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-node", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{NODE_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "node-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "node-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-python", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{PYTHON_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "python-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "python-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-php", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{PHP_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "php-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "php-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-laravel", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{LARAVEL_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "laravel-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "laravel-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-java", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{JAVA_SERVICE_URL}/api/data");
        var data = await response.Content.ReadAsStringAsync();

        return Results.Ok(new
        {
            service = serviceName,
            called = "java-test-app",
            response = data,
            status = (int)response.StatusCode
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            service = serviceName,
            called = "java-test-app",
            error = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/call-all", async (IHttpClientFactory httpClientFactory) =>
{
    var results = new
    {
        service = serviceName,
        chain = new List<object>()
    };

    var services = new[]
    {
        new { name = "go-test-app", url = GO_SERVICE_URL },
        new { name = "node-test-app", url = NODE_SERVICE_URL },
        new { name = "python-test-app", url = PYTHON_SERVICE_URL },
        new { name = "php-test-app", url = PHP_SERVICE_URL },
        new { name = "laravel-test-app", url = LARAVEL_SERVICE_URL },
        new { name = "java-test-app", url = JAVA_SERVICE_URL }
    };

    var client = httpClientFactory.CreateClient();

    foreach (var service in services)
    {
        try
        {
            var response = await client.GetAsync($"{service.url}/api/data");
            var data = await response.Content.ReadAsStringAsync();

            results.chain.Add(new
            {
                service = service.name,
                status = (int)response.StatusCode,
                response = data
            });
        }
        catch (Exception ex)
        {
            results.chain.Add(new
            {
                service = service.name,
                error = ex.Message
            });
        }
    }

    return Results.Ok(results);
});

// Cleanup on shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    sdk.Dispose();
});

Console.WriteLine($"Starting {serviceName} on http://localhost:8087");
app.Run("http://localhost:8087");
