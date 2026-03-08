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

    // Circuit breaker state (lock-protected)
    private readonly object _circuitBreakerLock = new();
    private readonly List<long> _cbFailureTimestamps = new();
    private string _cbState = "closed";
    private long _cbOpenedAt;
    private int _cbMaxFailures = 3;
    private long _cbWindowMs = 60000;
    private long _cbCooldownMs = 300000;
    private readonly List<Dictionary<string, object>> _pendingEvents = new();

    // Kill switch: server-initiated monitoring disable
    private volatile bool _killSwitchActive;
    private int _normalPollSeconds;

    // SSE (Server-Sent Events) real-time updates
    private string? _sseEndpoint;
    private volatile bool _sseActive;
    private CancellationTokenSource? _sseCts;

    // Opt-in capture limits (all disabled by default: 0 = unlimited)
    /// <summary>Max nesting depth for captured variables. 0 = unlimited (default).</summary>
    public int CaptureDepth { get; set; }

    /// <summary>Max serialized payload size in bytes. 0 = unlimited (default).</summary>
    public int MaxPayload { get; set; }

    /// <summary>Capture timeout in milliseconds. 0 = no timeout (default).</summary>
    public int CaptureTimeout { get; set; }

    public SnapshotClient(string apiKey, string baseURL, string serviceName, int pollIntervalSeconds = 30)
    {
        _apiKey = apiKey;
        _baseURL = baseURL;
        _serviceName = serviceName;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _securityDetector = new SensitiveDataDetector();
        _normalPollSeconds = pollIntervalSeconds;

        // Start polling timer
        _pollTimer = new Timer(
            _ => FetchActiveBreakpoints(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(pollIntervalSeconds)
        );
    }

    /// <summary>Configure circuit breaker thresholds (0 = use default).</summary>
    public void ConfigureCircuitBreaker(int maxFailures = 0, long windowMs = 0, long cooldownMs = 0)
    {
        lock (_circuitBreakerLock)
        {
            if (maxFailures > 0) _cbMaxFailures = maxFailures;
            if (windowMs > 0) _cbWindowMs = windowMs;
            if (cooldownMs > 0) _cbCooldownMs = cooldownMs;
        }
    }

    private bool CircuitBreakerShouldAllow()
    {
        lock (_circuitBreakerLock)
        {
            if (_cbState == "closed") return true;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now - _cbOpenedAt >= _cbCooldownMs)
            {
                _cbState = "closed";
                _cbFailureTimestamps.Clear();
                Debug.WriteLine("TraceKit: Code monitoring resumed");
                return true;
            }
            return false;
        }
    }

    private bool CircuitBreakerRecordFailure()
    {
        lock (_circuitBreakerLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _cbFailureTimestamps.Add(now);
            var cutoff = now - _cbWindowMs;
            _cbFailureTimestamps.RemoveAll(ts => ts <= cutoff);

            if (_cbFailureTimestamps.Count >= _cbMaxFailures && _cbState == "closed")
            {
                _cbState = "open";
                _cbOpenedAt = now;
                Debug.WriteLine($"TraceKit: Code monitoring paused ({_cbMaxFailures} capture failures in {_cbWindowMs / 1000}s). Auto-resumes in {_cbCooldownMs / 60000} min.");
                return true;
            }
            return false;
        }
    }

    private void QueueCircuitBreakerEvent()
    {
        lock (_pendingEvents)
        {
            _pendingEvents.Add(new Dictionary<string, object>
            {
                ["type"] = "circuit_breaker_tripped",
                ["service_name"] = _serviceName,
                ["failure_count"] = _cbMaxFailures,
                ["window_seconds"] = _cbWindowMs / 1000,
                ["cooldown_seconds"] = _cbCooldownMs / 1000,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            });
        }
    }

    /// <summary>
    /// Capture a snapshot at the caller's location.
    /// Crash isolation: catches all exceptions so TraceKit never crashes the host application.
    /// </summary>
    public void CaptureSnapshot(string label, Dictionary<string, object> variables,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string functionName = "")
    {
        try
        {
            // Kill switch: skip all capture when server has disabled monitoring
            if (_killSwitchActive) return;

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

            // Apply opt-in capture depth limit
            if (CaptureDepth > 0)
            {
                variables = LimitDepth(variables, 0);
            }

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
                TraceId: activity != null && activity.Recorded ? activity.TraceId.ToString() : null,
                SpanId: activity != null && activity.Recorded ? activity.SpanId.ToString() : null,
                Timestamp: DateTime.UtcNow
            );

            // Submit snapshot asynchronously (with optional timeout)
            if (CaptureTimeout > 0)
            {
                var task = Task.Run(() => SubmitSnapshot(snapshot));
                if (!task.Wait(TimeSpan.FromMilliseconds(CaptureTimeout)))
                {
                    Debug.WriteLine($"TraceKit: capture timeout exceeded ({CaptureTimeout}ms)");
                }
            }
            else
            {
                _ = Task.Run(() => SubmitSnapshot(snapshot));
            }
        }
        catch (Exception ex)
        {
            // Crash isolation: never let TraceKit errors propagate to the host application
            Debug.WriteLine($"TraceKit: error in CaptureSnapshot: {ex.Message}");
        }
    }

    /// <summary>Limit variable depth for opt-in capture depth limiting</summary>
    private Dictionary<string, object> LimitDepth(Dictionary<string, object> data, int currentDepth)
    {
        if (currentDepth >= CaptureDepth)
        {
            return new Dictionary<string, object>
            {
                ["_truncated"] = true,
                ["_depth"] = currentDepth
            };
        }

        var result = new Dictionary<string, object>();
        foreach (var kvp in data)
        {
            if (kvp.Value is Dictionary<string, object> nested)
            {
                result[kvp.Key] = LimitDepth(nested, currentDepth + 1);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
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

            if (result != null)
            {
                // Handle kill switch state (missing field = false for backward compat)
                var newKillState = result.KillSwitch == true;
                if (newKillState && !_killSwitchActive)
                {
                    Debug.WriteLine("TraceKit: Code monitoring disabled by server kill switch. Polling at reduced frequency.");
                    _pollTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
                }
                else if (!newKillState && _killSwitchActive)
                {
                    Debug.WriteLine("TraceKit: Code monitoring re-enabled by server.");
                    _pollTimer.Change(TimeSpan.FromSeconds(_normalPollSeconds), TimeSpan.FromSeconds(_normalPollSeconds));
                }
                _killSwitchActive = newKillState;

                // If kill-switched, close any active SSE connection
                if (_killSwitchActive && _sseActive)
                {
                    _sseCts?.Cancel();
                    _sseActive = false;
                    Debug.WriteLine("TraceKit: SSE connection closed due to kill switch");
                }

                // SSE auto-discovery: if sse_endpoint present and not already connected
                if (!string.IsNullOrEmpty(result.SseEndpoint) && !_sseActive && !_killSwitchActive
                    && result.Breakpoints != null && result.Breakpoints.Count > 0)
                {
                    _sseEndpoint = result.SseEndpoint;
                    _sseCts = new CancellationTokenSource();
                    var endpoint = result.SseEndpoint;
                    var ct = _sseCts.Token;
                    _ = Task.Run(() => ConnectSseAsync(endpoint, ct));
                }

                if (result.Breakpoints != null)
                {
                    UpdateBreakpointCache(result.Breakpoints);
                }
            }
        }
        catch
        {
            // Silently ignore errors fetching breakpoints
        }
    }

    /// <summary>
    /// Connect to SSE endpoint for real-time breakpoint updates.
    /// Falls back to polling if SSE connection fails or is interrupted.
    /// </summary>
    private async Task ConnectSseAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var fullUrl = $"{_baseURL}{endpoint}";
            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            request.Headers.Add("X-API-Key", _apiKey);
            request.Headers.Add("Accept", "text/event-stream");

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"TraceKit: SSE endpoint returned {(int)response.StatusCode}, falling back to polling");
                _sseActive = false;
                return;
            }

            _sseActive = true;
            Debug.WriteLine("TraceKit: SSE connection established for real-time breakpoint updates");

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var eventType = "";
            var dataBuffer = "";

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break; // Stream ended

                if (line.StartsWith("event:"))
                {
                    eventType = line[6..].Trim();
                }
                else if (line.StartsWith("data:"))
                {
                    if (dataBuffer.Length > 0) dataBuffer += "\n";
                    dataBuffer += line[5..].Trim();
                }
                else if (line == "")
                {
                    // Empty line = event boundary
                    if (eventType.Length > 0 && dataBuffer.Length > 0)
                    {
                        HandleSseEvent(eventType, dataBuffer);
                    }
                    eventType = "";
                    dataBuffer = "";
                }
            }

            Debug.WriteLine("TraceKit: SSE connection closed, falling back to polling");
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TraceKit: SSE connection lost, falling back to polling: {ex.Message}");
        }
        finally
        {
            _sseActive = false;
        }
    }

    /// <summary>Process a single SSE event.</summary>
    private void HandleSseEvent(string eventType, string data)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            switch (eventType)
            {
                case "init":
                {
                    var initData = JsonSerializer.Deserialize<BreakpointsResponse>(data, options);
                    if (initData?.Breakpoints != null)
                    {
                        UpdateBreakpointCache(initData.Breakpoints);
                    }
                    _killSwitchActive = initData?.KillSwitch == true;
                    if (_killSwitchActive)
                    {
                        _sseCts?.Cancel();
                    }
                    Debug.WriteLine($"TraceKit: SSE init received, {initData?.Breakpoints?.Count ?? 0} breakpoints loaded");
                    break;
                }

                case "breakpoint_created":
                case "breakpoint_updated":
                {
                    var bp = JsonSerializer.Deserialize<BreakpointConfig>(data, options);
                    if (bp != null)
                    {
                        if (!string.IsNullOrEmpty(bp.Label) && !string.IsNullOrEmpty(bp.FunctionName))
                        {
                            _breakpointsCache[$"{bp.FunctionName}:{bp.Label}"] = bp;
                        }
                        _breakpointsCache[$"{bp.FilePath}:{bp.LineNumber}"] = bp;
                        Debug.WriteLine($"TraceKit: SSE breakpoint {eventType}: {bp.Id}");
                    }
                    break;
                }

                case "breakpoint_deleted":
                {
                    using var doc = JsonDocument.Parse(data);
                    var bpId = doc.RootElement.GetProperty("id").GetString();
                    if (bpId != null)
                    {
                        var keysToRemove = _breakpointsCache
                            .Where(kvp => kvp.Value.Id == bpId)
                            .Select(kvp => kvp.Key)
                            .ToList();
                        foreach (var key in keysToRemove)
                        {
                            _breakpointsCache.TryRemove(key, out _);
                        }
                        Debug.WriteLine($"TraceKit: SSE breakpoint deleted: {bpId}");
                    }
                    break;
                }

                case "kill_switch":
                {
                    using var doc = JsonDocument.Parse(data);
                    _killSwitchActive = doc.RootElement.GetProperty("enabled").GetBoolean();
                    if (_killSwitchActive)
                    {
                        Debug.WriteLine("TraceKit: Kill switch enabled via SSE, closing connection");
                        _sseCts?.Cancel();
                    }
                    break;
                }

                case "heartbeat":
                    // No action needed -- keeps connection alive
                    break;

                default:
                    Debug.WriteLine($"TraceKit: unknown SSE event type: {eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TraceKit: error handling SSE event {eventType}: {ex.Message}");
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
        // Circuit breaker check
        if (!CircuitBreakerShouldAllow()) return;

        try
        {
            string json;
            try
            {
                json = JsonSerializer.Serialize(snapshot);
            }
            catch (Exception ex)
            {
                // Serialization error -- do NOT count as circuit breaker failure
                Debug.WriteLine($"TraceKit: serialization error: {ex.Message}");
                snapshot = snapshot with
                {
                    Variables = new Dictionary<string, object>
                    {
                        ["_error"] = $"serialization failed: {ex.Message}"
                    }
                };
                json = JsonSerializer.Serialize(snapshot);
            }

            // Apply opt-in max payload limit
            if (MaxPayload > 0 && System.Text.Encoding.UTF8.GetByteCount(json) > MaxPayload)
            {
                snapshot = snapshot with
                {
                    Variables = new Dictionary<string, object>
                    {
                        ["_truncated"] = true,
                        ["_payload_size"] = System.Text.Encoding.UTF8.GetByteCount(json),
                        ["_max_payload"] = MaxPayload
                    }
                };
                json = JsonSerializer.Serialize(snapshot);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseURL}/sdk/snapshots/capture")
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-API-Key", _apiKey);

            var response = _httpClient.Send(request);
            if ((int)response.StatusCode >= 500)
            {
                // Server error -- count as circuit breaker failure
                if (CircuitBreakerRecordFailure()) QueueCircuitBreakerEvent();
            }
        }
        catch (HttpRequestException ex)
        {
            // Network error -- count as circuit breaker failure
            Debug.WriteLine($"TraceKit: error submitting snapshot: {ex.Message}");
            if (CircuitBreakerRecordFailure()) QueueCircuitBreakerEvent();
        }
        catch (TaskCanceledException ex)
        {
            // Timeout -- count as circuit breaker failure
            Debug.WriteLine($"TraceKit: timeout submitting snapshot: {ex.Message}");
            if (CircuitBreakerRecordFailure()) QueueCircuitBreakerEvent();
        }
        catch (Exception ex)
        {
            // Crash isolation: never propagate errors
            Debug.WriteLine($"TraceKit: error submitting snapshot: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sseCts?.Cancel();
        _sseCts?.Dispose();
        _pollTimer?.Dispose();
        _httpClient?.Dispose();
    }
}
