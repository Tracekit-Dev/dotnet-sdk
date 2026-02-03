namespace TraceKit.Core.Snapshots;

internal record BreakpointConfig(
    string Id,
    string FilePath,
    int LineNumber,
    string? FunctionName,
    string? Label,
    bool Enabled,
    int MaxCaptures,
    int CaptureCount,
    DateTime? ExpireAt
);

internal record BreakpointsResponse(
    List<BreakpointConfig> Breakpoints
);

internal record Snapshot(
    string BreakpointId,
    string ServiceName,
    string FilePath,
    string? FunctionName,
    string? Label,
    int LineNumber,
    Dictionary<string, object> Variables,
    List<object> SecurityFlags,
    string StackTrace,
    string? TraceId,
    string? SpanId,
    DateTime Timestamp
);

internal record BreakpointRegistration(
    string ServiceName,
    string FilePath,
    int LineNumber,
    string? FunctionName,
    string? Label
);
