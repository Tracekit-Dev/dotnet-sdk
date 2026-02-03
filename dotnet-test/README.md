# TraceKit .NET Test App

A comprehensive .NET 8 example application demonstrating the TraceKit .NET SDK for distributed tracing, performance monitoring, and code debugging.

## Features

This test app showcases:

- ✅ **Automatic HTTP request tracing** - All requests traced via OpenTelemetry
- ✅ **Code monitoring with snapshots** - Live debugging with variable inspection
- ✅ **Metrics API** - Counter, Gauge, and Histogram metrics with OTLP export
- ✅ **Cross-service communication** - CLIENT spans for outgoing HTTP calls
- ✅ **Service dependency mapping** - Automatic service graph generation
- ✅ **Error tracking** - Capture exceptions with full trace context

## Prerequisites

- .NET SDK 8.0 or higher
- TraceKit account and API key (get one at [tracekit.dev](https://tracekit.dev))
- (Optional) Other test services running for cross-service testing

## Setup

### 1. Configure Environment

```bash
# Copy the example environment file
cp .env.example .env

# Edit .env and add your TraceKit API key
# TRACEKIT_API_KEY=your-api-key-here
```

Get your API key from: https://app.tracekit.dev

### 2. Run the Application

```bash
# Run the application
dotnet run --urls http://localhost:8087

# The server will start on http://localhost:8087
```

## Available Endpoints

| Endpoint | Method | Description | TraceKit Features Demonstrated |
|----------|--------|-------------|-------------------------------|
| `/` | GET | Service info & endpoint list | Basic HTTP tracing |
| `/health` | GET | Health check | Simple status endpoint |
| `/test` | GET | Code monitoring test | Snapshot capture, variable inspection |
| `/error-test` | GET | Trigger an error | Error recording with trace context |
| `/checkout` | GET | Checkout simulation | Code snapshots |
| `/api/data` | GET | Data endpoint | Called by other services |
| `/api/call-go` | GET | Call Go service | CLIENT spans, cross-service tracing |
| `/api/call-node` | GET | Call Node.js service | Distributed tracing |
| `/api/call-python` | GET | Call Python service | Service dependency mapping |
| `/api/call-php` | GET | Call PHP service | Cross-service communication |
| `/api/call-laravel` | GET | Call Laravel service | Distributed tracing |
| `/api/call-java` | GET | Call Java service | Cross-service communication |
| `/api/call-all` | GET | Call all services | Multi-service distributed trace |

## Testing

### Quick Test

```bash
# Test all endpoints
curl http://localhost:8087/
curl http://localhost:8087/health
curl http://localhost:8087/test
curl http://localhost:8087/checkout
```

### Code Monitoring Test

The `/test` endpoint demonstrates code snapshot capture:

```bash
curl http://localhost:8087/test
```

This will capture three snapshots:
1. **test-route-entry** - Route entry point with request metadata
2. **test-processing** - During processing with user_id and cart_total
3. **test-complete** - Final state with status

View these snapshots in your TraceKit dashboard under Code Monitoring.

### Cross-Service Communication Test

Requires other test services to be running:

```bash
# Test calling Go service (requires go-test-app on :8082)
curl http://localhost:8087/api/call-go

# Test calling Node.js service (requires node-test on :8084)
curl http://localhost:8087/api/call-node

# Test calling Python service (requires python-test on :5001)
curl http://localhost:8087/api/call-python

# Test calling PHP service (requires php-test on :8086)
curl http://localhost:8087/api/call-php

# Test calling Laravel service (requires laravel-test on :8083)
curl http://localhost:8087/api/call-laravel

# Test calling Java service (requires java-test on :8080)
curl http://localhost:8087/api/call-java

# Test calling ALL services (full distributed trace)
curl http://localhost:8087/api/call-all
```

## What Gets Traced

### Automatic Tracing

The TraceKit SDK automatically captures:

- **HTTP Requests**
  - Request method, path, headers
  - Response status code and size
  - Request duration

- **HTTP Client Calls**
  - Outgoing HTTP requests
  - Distributed trace propagation
  - Service dependency mapping

### Code Monitoring Snapshots

Use `sdk.CaptureSnapshot()` to capture variable state:

```csharp
sdk.CaptureSnapshot("checkpoint-name", new Dictionary<string, object>
{
    ["userId"] = 123,
    ["cartTotal"] = 99.99,
    ["status"] = "processing"
});
```

### Custom Metrics

Track application metrics with Counter, Gauge, and Histogram types:

```csharp
var requestCounter = sdk.Counter("http.requests.total", new() { ["service"] = "dotnet-test" });
var activeGauge = sdk.Gauge("http.requests.active");
var durationHistogram = sdk.Histogram("http.request.duration", new() { ["unit"] = "ms" });

requestCounter.Inc();
activeGauge.Set(42);
durationHistogram.Record(123.45);
```

**Metrics tracked by this app:**
- `http.requests.total` - Total number of HTTP requests (Counter)
- `http.requests.active` - Currently active requests (Gauge)
- `http.request.duration` - Request duration in milliseconds (Histogram)
- `http.errors.total` - Total number of HTTP errors (Counter)

Metrics are automatically exported to TraceKit using OTLP protocol every 10 seconds or when 100 metrics are buffered.

## Viewing Traces

### TraceKit Dashboard
View traces at: https://app.tracekit.dev/traces

### Code Monitoring
View snapshots at: https://app.tracekit.dev

## Troubleshooting

### "TRACEKIT_API_KEY not configured"
- Ensure you've set the environment variable
- Check that your API key is valid

### Traces not appearing in dashboard
- Verify `TRACEKIT_API_KEY` is set correctly
- Check network connectivity to app.tracekit.dev

### Cross-service calls timing out
- Ensure other test services are running:
  - go-test-app: http://localhost:8082
  - node-test: http://localhost:8084
  - python-test: http://localhost:5001
  - php-test: http://localhost:8086
  - laravel-test: http://localhost:8083
  - java-test: http://localhost:8080

## Learn More

- [TraceKit Documentation](https://docs.tracekit.dev)
- [TraceKit .NET SDK](https://github.com/Tracekit-Dev/dotnet-sdk)
- [.NET Documentation](https://learn.microsoft.com/en-us/dotnet/)

## License

MIT License - See main TraceKit repository for details.
