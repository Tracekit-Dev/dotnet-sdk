namespace TraceKit.Core.Snapshots;

internal record BreakpointConfig(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("file_path")]
    string FilePath,
    [property: System.Text.Json.Serialization.JsonPropertyName("line_number")]
    int LineNumber,
    [property: System.Text.Json.Serialization.JsonPropertyName("function_name")]
    string? FunctionName,
    [property: System.Text.Json.Serialization.JsonPropertyName("label")]
    string? Label,
    [property: System.Text.Json.Serialization.JsonPropertyName("enabled")]
    bool Enabled,
    [property: System.Text.Json.Serialization.JsonPropertyName("max_captures")]
    int MaxCaptures,
    [property: System.Text.Json.Serialization.JsonPropertyName("capture_count")]
    int CaptureCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("expire_at")]
    DateTime? ExpireAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("condition")]
    string? Condition = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("condition_eval")]
    string? ConditionEval = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("mode")]
    string? Mode = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("stack_depth")]
    int? StackDepth = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("max_depth")]
    int? MaxDepth = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("max_payload_bytes")]
    int? MaxPayloadBytes = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("capture_expressions")]
    List<string>? CaptureExpressions = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("idle_timeout_hours")]
    int? IdleTimeoutHours = null
);

internal record BreakpointsResponse(
    List<BreakpointConfig> Breakpoints,
    [property: System.Text.Json.Serialization.JsonPropertyName("kill_switch")]
    bool? KillSwitch = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("sse_endpoint")]
    string? SseEndpoint = null
);

internal record Snapshot(
    [property: System.Text.Json.Serialization.JsonPropertyName("breakpoint_id")]
    string BreakpointId,
    [property: System.Text.Json.Serialization.JsonPropertyName("service_name")]
    string ServiceName,
    [property: System.Text.Json.Serialization.JsonPropertyName("file_path")]
    string FilePath,
    [property: System.Text.Json.Serialization.JsonPropertyName("function_name")]
    string? FunctionName,
    [property: System.Text.Json.Serialization.JsonPropertyName("label")]
    string? Label,
    [property: System.Text.Json.Serialization.JsonPropertyName("line_number")]
    int LineNumber,
    [property: System.Text.Json.Serialization.JsonPropertyName("variables")]
    Dictionary<string, object> Variables,
    [property: System.Text.Json.Serialization.JsonPropertyName("security_flags")]
    List<object> SecurityFlags,
    [property: System.Text.Json.Serialization.JsonPropertyName("stack_trace")]
    string StackTrace,
    [property: System.Text.Json.Serialization.JsonPropertyName("trace_id")]
    string? TraceId,
    [property: System.Text.Json.Serialization.JsonPropertyName("span_id")]
    string? SpanId,
    [property: System.Text.Json.Serialization.JsonPropertyName("captured_at")]
    DateTime Timestamp,
    [property: System.Text.Json.Serialization.JsonPropertyName("expression_results")]
    Dictionary<string, object?>? ExpressionResults = null
);

internal record BreakpointRegistration(
    [property: System.Text.Json.Serialization.JsonPropertyName("service_name")]
    string ServiceName,
    [property: System.Text.Json.Serialization.JsonPropertyName("file_path")]
    string FilePath,
    [property: System.Text.Json.Serialization.JsonPropertyName("line_number")]
    int LineNumber,
    [property: System.Text.Json.Serialization.JsonPropertyName("function_name")]
    string? FunctionName,
    [property: System.Text.Json.Serialization.JsonPropertyName("label")]
    string? Label
);
