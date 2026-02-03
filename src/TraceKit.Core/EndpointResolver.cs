using System.Text.RegularExpressions;

namespace TraceKit.Core;

/// <summary>
/// Resolves endpoint URLs for different TraceKit services (traces, metrics, snapshots).
/// Implements the same logic as Go and Java SDKs for consistency across all platforms.
/// </summary>
public static class EndpointResolver
{
    /// <summary>
    /// Resolves a full endpoint URL from a base endpoint and path.
    /// </summary>
    /// <param name="endpoint">The base endpoint (can be host, host with scheme, or full URL)</param>
    /// <param name="path">The path to append (e.g., "/v1/traces", "/v1/metrics", or "")</param>
    /// <param name="useSSL">Whether to use HTTPS (ignored if endpoint already has a scheme)</param>
    /// <returns>The resolved endpoint URL</returns>
    /// <example>
    /// ResolveEndpoint("app.tracekit.dev", "/v1/traces", true) → "https://app.tracekit.dev/v1/traces"
    /// ResolveEndpoint("http://localhost:8081", "/v1/traces", true) → "http://localhost:8081/v1/traces"
    /// ResolveEndpoint("https://app.tracekit.dev/v1/traces", "/v1/metrics", true) → "https://app.tracekit.dev/v1/metrics"
    /// </example>
    public static string ResolveEndpoint(string endpoint, string path, bool useSSL)
    {
        // If endpoint has a scheme (http:// or https://)
        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Remove trailing slash
            endpoint = endpoint.TrimEnd('/');

            // Check if endpoint has a path component (anything after the host)
            var withoutScheme = Regex.Replace(endpoint, "^https?://", "", RegexOptions.IgnoreCase);

            if (withoutScheme.Contains('/'))
            {
                // Endpoint has a path component - extract base and append correct path
                var baseUrl = ExtractBaseURL(endpoint);
                return string.IsNullOrEmpty(path) ? baseUrl : baseUrl + path;
            }

            // Just host with scheme, add the path
            return endpoint + path;
        }

        // No scheme provided - build URL with scheme
        var scheme = useSSL ? "https://" : "http://";
        endpoint = endpoint.TrimEnd('/');
        return scheme + endpoint + path;
    }

    /// <summary>
    /// Extracts base URL (scheme + host + port) from full URL.
    /// Always strips any path component, regardless of what it is.
    /// </summary>
    /// <param name="fullURL">The full URL to extract base from</param>
    /// <returns>The base URL (scheme + host + port)</returns>
    /// <example>
    /// ExtractBaseURL("https://app.tracekit.dev/v1/traces") → "https://app.tracekit.dev"
    /// ExtractBaseURL("http://localhost:8081/custom/path") → "http://localhost:8081"
    /// </example>
    public static string ExtractBaseURL(string fullURL)
    {
        var match = Regex.Match(fullURL, @"^(https?://[^/]+)");
        return match.Success ? match.Groups[1].Value : fullURL;
    }
}
