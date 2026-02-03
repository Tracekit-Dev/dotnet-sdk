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
            var lineKey = $"{filePath}:{lineNumber}";
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

        // Get trace context from current Activity (OpenTelemetry)
        var activity = Activity.Current;
        var stackTrace = new StackTrace(true).ToString();

        var snapshot = new Snapshot(
            BreakpointId: breakpoint.Id,
            ServiceName: _serviceName,
            FilePath: filePath,
            FunctionName: functionName,
            Label: label,
            LineNumber: lineNumber,
            Variables: scanResult.SanitizedVariables,
            SecurityFlags: scanResult.SecurityFlags.Cast<object>().ToList(),
            StackTrace: stackTrace,
            TraceId: activity?.TraceId.ToString(),
            SpanId: activity?.SpanId.ToString(),
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
        catch
        {
            // Silently ignore errors fetching breakpoints
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

            var lineKey = $"{bp.FilePath}:{bp.LineNumber}";
            _breakpointsCache[lineKey] = bp;
        }
    }

    private void AutoRegisterBreakpoint(string filePath, int lineNumber, string functionName, string label)
    {
        var regKey = $"{functionName}:{label}";
        if (_registrationCache.Contains(regKey)) return;

        _registrationCache.Add(regKey);

        _ = Task.Run(async () =>
        {
            try
            {
                var registration = new BreakpointRegistration(
                    ServiceName: _serviceName,
                    FilePath: filePath,
                    LineNumber: lineNumber,
                    FunctionName: functionName,
                    Label: label
                );

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseURL}/sdk/snapshots/auto-register")
                {
                    Content = JsonContent.Create(registration)
                };
                request.Headers.Add("X-API-Key", _apiKey);

                var response = _httpClient.Send(request);

                // Refresh breakpoints cache after successful registration
                if (response.IsSuccessStatusCode)
                {
                    await Task.Delay(500); // Small delay for backend processing
                    FetchActiveBreakpoints();
                }
            }
            catch
            {
                // Silently ignore auto-registration errors
            }
        });
    }

    private void SubmitSnapshot(Snapshot snapshot)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseURL}/sdk/snapshots/capture")
            {
                Content = JsonContent.Create(snapshot)
            };
            request.Headers.Add("X-API-Key", _apiKey);

            _httpClient.Send(request);
        }
        catch
        {
            // Silently ignore snapshot submission errors
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
