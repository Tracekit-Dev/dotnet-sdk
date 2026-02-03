# TraceKit .NET SDK

[![NuGet](https://img.shields.io/nuget/v/TraceKit.Core.svg)](https://www.nuget.org/packages/TraceKit.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-6.0%20%7C%207.0%20%7C%208.0-blue.svg)](https://dotnet.microsoft.com/)

Official .NET SDK for TraceKit APM - OpenTelemetry-based distributed tracing, metrics collection, and application performance monitoring for .NET applications.

## 🚧 Status: Under Development

This SDK is currently in active development. See [DOTNET_SDK_IMPLEMENTATION_PLAN.md](../plans/DOTNET_SDK_IMPLEMENTATION_PLAN.md) for the full implementation roadmap.

**Target Release:** v0.1.0

## Overview

TraceKit .NET SDK provides production-ready distributed tracing, metrics, and code monitoring capabilities for .NET and ASP.NET Core applications. Built on OpenTelemetry standards, it offers seamless integration with ASP.NET Core, automatic local development support, comprehensive security scanning, and a lightweight metrics API for tracking application performance.

## Features (Planned)

- **OpenTelemetry-Native**: Built on OpenTelemetry 1.7.0 for maximum compatibility
- **Distributed Tracing**: Full support for distributed trace propagation across microservices
- **Metrics API**: Counter, Gauge, and Histogram metrics with automatic OTLP export
- **Code Monitoring**: Live production debugging with non-breaking snapshots
- **Security Scanning**: Automatic detection of sensitive data (PII, credentials)
- **Local UI Auto-Detection**: Automatically sends traces to local TraceKit UI
- **ASP.NET Core Integration**: Zero-configuration middleware and DI support
- **HttpClient Instrumentation**: Automatic client-side span creation
- **Production-Ready**: Comprehensive error handling and graceful shutdown

## Installation (Coming Soon)

### NuGet

For ASP.NET Core applications:

```bash
dotnet add package TraceKit.AspNetCore
```

For vanilla .NET applications:

```bash
dotnet add package TraceKit.Core
```

## Quick Start (Coming Soon)

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add TraceKit
builder.Services.AddTracekit(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("TRACEKIT_API_KEY");
    options.ServiceName = "my-service";
    options.Environment = "production";
    options.EnableCodeMonitoring = true;
});

var app = builder.Build();

// Use TraceKit middleware
app.UseTracekit();

app.MapGet("/api/users", (TracekitSDK sdk) =>
{
    var counter = sdk.Counter("http.requests.total");
    counter.Inc();

    return Results.Ok(new { message = "Hello" });
});

app.Run();
```

### Configuration (appsettings.json)

```json
{
  "Tracekit": {
    "Enabled": true,
    "ApiKey": "ctxio_abc123...",
    "ServiceName": "my-api",
    "Environment": "production",
    "Endpoint": "app.tracekit.dev",
    "EnableCodeMonitoring": true
  }
}
```

## Metrics API

```csharp
var sdk = TracekitSDK.Create(config);

// Initialize metrics
var requestCounter = sdk.Counter("http.requests.total",
    new() { ["service"] = "api" });
var activeGauge = sdk.Gauge("http.requests.active");
var durationHistogram = sdk.Histogram("http.request.duration",
    new() { ["unit"] = "ms" });

// Use metrics
requestCounter.Inc();
activeGauge.Set(42);
durationHistogram.Record(123.45);
```

## Code Monitoring

```csharp
// Capture snapshot with variable state
TracekitSDK.CaptureSnapshot("checkout-start", new()
{
    ["userId"] = 123,
    ["amount"] = 99.99,
    ["status"] = "processing"
});
```

## Project Structure

```
dotnet-sdk/
├── src/
│   ├── TraceKit.Core/              # Core SDK
│   └── TraceKit.AspNetCore/        # ASP.NET Core integration
├── tests/
│   └── TraceKit.Core.Tests/        # Unit tests
├── examples/
│   └── AspNetCore.WebApi/          # Example application
├── dotnet-test/                    # Cross-service test app
└── README.md
```

## Development Setup

### Prerequisites

- .NET SDK 8.0 or higher
- Git
- TraceKit API key (get one at [tracekit.dev](https://tracekit.dev))

### Building from Source

```bash
# Clone the repository
git clone git@github.com:Tracekit-Dev/dotnet-sdk.git
cd dotnet-sdk

# Build all projects
dotnet build

# Run tests
dotnet test

# Run example app
cd examples/AspNetCore.WebApi
dotnet run
```

## Requirements

- **.NET**: 6.0, 7.0, or 8.0
- **Build Tools**: .NET SDK
- **Dependencies**: Managed via NuGet

## Documentation

- [Implementation Plan](../plans/DOTNET_SDK_IMPLEMENTATION_PLAN.md)
- [TraceKit Documentation](https://docs.tracekit.dev) (Coming Soon)
- [API Reference](https://docs.tracekit.dev/dotnet) (Coming Soon)

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for:
- How to build and test
- Code style guidelines
- Pull request process
- Development workflow

## Support

- **Documentation**: https://docs.tracekit.dev/dotnet (Coming Soon)
- **Issues**: https://github.com/Tracekit-Dev/dotnet-sdk/issues
- **Email**: support@tracekit.dev

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

Built on [OpenTelemetry](https://opentelemetry.io/) - the industry standard for observability.

---

**Repository**: git@github.com:Tracekit-Dev/dotnet-sdk.git
**Target Version**: v0.1.0
**Status**: In Development
