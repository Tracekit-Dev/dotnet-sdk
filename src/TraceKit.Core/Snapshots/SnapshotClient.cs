using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TraceKit.Core.Security;

namespace TraceKit.Core.Snapshots;

/// <summary>
/// Client for code monitoring - polls breakpoints and captures snapshots.
/// </summary>
public sealed class SnapshotClient : IDisposable
{
    private readonly string _apiKey;
    private readonly string _baseURL;
    private readonly string _serviceName;
    private readonly HttpClient _httpClient;
    private readonly SensitiveDataDetector _securityDetector;
    private readonly ConcurrentDictionary<string, BreakpointConfig> _breakpointsCache = new();
    private readonly HashSet<string> _registrationCache = new();
    private readonly Timer _pollTimer;
    private bool _disposed;

    public SnapshotClient(string apiKey, string baseURL, string serviceName, int pollIntervalSeconds = 30)
    {
        _apiKey = apiKey;
        _baseURL = baseURL;
        _serviceName = serviceName;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _securityDetector = new SensitiveDataDetector();

        // Start polling timer
        _pollTimer = new Timer(
            _ => FetchActiveBreakpoints(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(pollIntervalSeconds)
        );

        Console.WriteLine($"Snapshot client started for service: {_serviceName}");
    }

    public void CaptureSnapshot(string label, Dictionary<string, object> variables,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string functionName = "")
    {
        // Auto-register breakpoint
        AutoRegisterBreakpoint(filePath, lineNumber, functionName, label);

        // Check if breakpoint is active
        var locationKey = $"{functionName}:{label}";
        if (!_breakpointsCache.TryGetValue(locationKey, out var breakpoint))
        {
            var lineKey = $"{Path.GetFileName(filePath)}:{lineNumber}";
            if (!_breakpointsCache.TryGetValue(lineKey, out breakpoint))
            {
                return; // No active breakpoint
            }
        }

        if (!breakpoint.Enabled) return;
        if (breakpoint.ExpireAt.HasValue && DateTime.UtcNow > breakpoint.ExpireAt) return;
        if (breakpoint.MaxCaptures > 0 && breakpoint.CaptureCount >= breakpoint.MaxCaptures) return;

        // Scan for security issues
        var scanResult = _securityDetector.Scan(variables);

        // Get trace context (simplified - would use actual OpenTelemetry context)
        var stackTrace = new StackTrace(true).ToString();

        var snapshot = new Snapshot(
            BreakpointId: breakpoint.Id,
            ServiceName: _serviceName,
            FilePath: Path.GetFileName(filePath),
            FunctionName: functionName,
            Label: label,
            LineNumber: lineNumber,
            Variables: scanResult.SanitizedVariables,
            SecurityFlags: scanResult.SecurityFlags.Cast<object>().ToList(),
            StackTrace: stackTrace,
            TraceId: null, // Would be populated from OpenTelemetry context
            SpanId: null,
            Timestamp: DateTime.UtcNow
        );

        // Submit snapshot asynchronously
        _ = Task.Run(() => SubmitSnapshot(snapshot));
    }

    private void FetchActiveBreakpoints()
    {
        try
        {
            var url = $"{_baseURL}/sdk/snapshots/active/{_serviceName}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-API-Key", _apiKey);

            var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch breakpoints: HTTP {response.StatusCode}");
                return;
            }

            var result = JsonSerializer.Deserialize<BreakpointsResponse>(
                response.Content.ReadAsStream(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Breakpoints != null)
            {
                UpdateBreakpointCache(result.Breakpoints);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching breakpoints: {ex.Message}");
        }
    }

    private void UpdateBreakpointCache(List<BreakpointConfig> breakpoints)
    {
        _breakpointsCache.Clear();

        foreach (var bp in breakpoints)
        {
            if (!string.IsNullOrEmpty(bp.Label) && !string.IsNullOrEmpty(bp.FunctionName))
            {
                var labelKey = $"{bp.FunctionName}:{bp.Label}";
                _breakpointsCache[labelKey] = bp;
            }

            var lineKey = $"{Path.GetFileName(bp.FilePath)}:{bp.LineNumber}";
            _breakpointsCache[lineKey] = bp;
        }

        Console.WriteLine($"Updated breakpoint cache: {breakpoints.Count} active breakpoints");
    }

    private void AutoRegisterBreakpoint(string filePath, int lineNumber, string functionName, string label)
    {
        var regKey = $"{functionName}:{label}";
        if (_registrationCache.Contains(regKey)) return;

        _registrationCache.Add(regKey);
        Console.WriteLine($"Auto-registering breakpoint: {label} at {filePath}:{lineNumber}");

        _ = Task.Run(() =>
        {
            try
            {
                var registration = new BreakpointRegistration(
                    ServiceName: _serviceName,
                    FilePath: Path.GetFileName(filePath),
                    LineNumber: lineNumber,
                    FunctionName: functionName,
                    Label: label
                );

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseURL}/sdk/snapshots/register")
                {
                    Content = JsonContent.Create(registration)
                };
                request.Headers.Add("X-API-Key", _apiKey);

                _httpClient.Send(request);
            }
            catch { }
        });
    }

    private void SubmitSnapshot(Snapshot snapshot)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseURL}/sdk/snapshots")
            {
                Content = JsonContent.Create(snapshot)
            };
            request.Headers.Add("X-API-Key", _apiKey);

            var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to submit snapshot: HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error submitting snapshot: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        _httpClient?.Dispose();
    }
}
