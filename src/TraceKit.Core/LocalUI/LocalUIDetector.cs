namespace TraceKit.Core.LocalUI;

/// <summary>
/// Detects if TraceKit Local UI is running and provides the endpoint.
/// </summary>
public sealed class LocalUIDetector
{
    private readonly int _port;

    public LocalUIDetector(int port = 9999)
    {
        _port = port;
    }

    /// <summary>
    /// Checks if Local UI is running by attempting a health check.
    /// </summary>
    public bool IsLocalUIRunning()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var response = client.GetAsync($"http://localhost:{_port}/api/health").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the Local UI endpoint if it's running, otherwise null.
    /// </summary>
    public string? GetLocalUIEndpoint()
    {
        return IsLocalUIRunning() ? $"http://localhost:{_port}" : null;
    }
}
