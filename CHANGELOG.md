# Changelog

All notable changes to the TraceKit .NET SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-02-03

### Added

#### Core SDK (TraceKit.Core)
- **Configuration & Setup**
  - `TracekitConfig` with fluent builder pattern for SDK configuration
  - `EndpointResolver` for flexible endpoint handling (host-only, full URLs, custom paths)
  - `LocalUIDetector` for automatic local development environment detection
  - Multi-target support for .NET 6.0, 7.0, and 8.0

- **Distributed Tracing**
  - Full OpenTelemetry integration for distributed tracing
  - OTLP/HTTP exporter for traces
  - Automatic span propagation with W3C Trace Context
  - Support for trace_id and span_id in all operations

- **Metrics API**
  - `Counter` metric type for monotonically increasing values
  - `Gauge` metric type for point-in-time measurements
  - `Histogram` metric type for value distributions
  - `MetricsRegistry` with automatic buffering (100 metrics or 10 seconds)
  - `MetricsExporter` with OTLP JSON format support
  - Automatic tag/label support for all metric types

- **Code Monitoring**
  - `SnapshotClient` with 30-second polling for active breakpoints
  - `CaptureSnapshot()` API for capturing variable state
  - Automatic breakpoint registration
  - Stack trace capture with caller information
  - Integration with distributed tracing (trace_id, span_id)
  - CallerAttributes support for automatic file/line/function detection

- **Security**
  - `SensitiveDataDetector` for automatic PII and credential detection
  - PII detection: emails, SSNs, credit cards, phone numbers
  - Credential detection: API keys, AWS keys, Stripe keys, passwords, JWTs
  - Automatic redaction with `[REDACTED]` replacement
  - Security flags with severity levels (low, medium, high, critical)
  - C# source generators with `[GeneratedRegex]` for performance

- **HTTP Client Instrumentation**
  - Automatic tracing of outgoing HTTP requests
  - OpenTelemetry HttpClient instrumentation
  - Distributed trace propagation to downstream services

#### ASP.NET Core Integration (TraceKit.AspNetCore)
- **Middleware & Extensions**
  - `TracekitMiddleware` for automatic HTTP request metrics tracking
  - `AddTracekit()` service collection extensions for DI registration
  - `UseTracekit()` application builder extension for middleware
  - `AddTracekitInstrumentation()` for HttpClient enhancement

- **Configuration**
  - `TracekitOptions` with validation
  - Support for programmatic configuration via `Action<TracekitOptions>`
  - Support for appsettings.json configuration
  - Support for combining both configuration sources

- **Automatic Metrics**
  - `http.server.requests` - Total HTTP requests with method and route tags
  - `http.server.active_requests` - Currently active requests gauge
  - `http.server.request.duration` - Request duration histogram
  - `http.server.errors` - Error count with status code and error type

#### Test Application (dotnet-test)
- Comprehensive test application running on port 8087
- All standard endpoints: `/`, `/health`, `/test`, `/checkout`, `/error-test`
- Cross-service communication endpoints for Go, Node.js, Python, PHP, Laravel, Java
- Complete metrics tracking demonstration
- Code monitoring with snapshot examples
- `.env.example` for configuration

#### Testing
- Unit tests for `EndpointResolver` (15 test cases)
- Full endpoint resolution coverage
- Test project setup with xUnit

### Technical Details

- **OpenTelemetry Version**: 1.7.0
- **Target Frameworks**: .NET 6.0, 7.0, 8.0
- **OTLP Protocol**: HTTP/JSON format
- **Default Endpoint**: app.tracekit.dev
- **Metrics Export**: Automatic at 100 metrics or 10 seconds
- **Snapshot Polling**: Every 30 seconds
- **Local UI Port**: 9999 (configurable)

### Endpoints

All SDKs derive endpoints from a single base:
- Traces: `{endpoint}/v1/traces` (OTLP HTTP POST)
- Metrics: `{endpoint}/v1/metrics` (OTLP HTTP POST)
- Snapshots Poll: `{endpoint}/sdk/snapshots/active/{serviceName}` (GET)
- Snapshots Submit: `{endpoint}/sdk/snapshots` (POST)
- Snapshots Register: `{endpoint}/sdk/snapshots/register` (POST)

### Known Limitations

- Snapshot capture requires CallerAttributes, limiting use in some reflection scenarios
- Metrics export is fire-and-forget (no retry logic in v0.1.0)
- Local UI detection is HTTP-only (no HTTPS support)

### Breaking Changes

None - this is the initial release.

## [Unreleased]

### Planned Features
- Retry logic for metrics and trace export
- Custom sampling strategies
- Advanced filtering options
- Additional ASP.NET Core features (exception handling, response compression integration)
- Support for .NET Framework 4.8+

---

[0.1.0]: https://github.com/Tracekit-Dev/dotnet-sdk/releases/tag/v0.1.0
