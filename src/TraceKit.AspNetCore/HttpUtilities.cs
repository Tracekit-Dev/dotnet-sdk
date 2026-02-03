using Microsoft.AspNetCore.Http;
using System.Net;

namespace TraceKit.AspNetCore;

/// <summary>
/// Utilities for extracting HTTP request information
/// </summary>
public static class HttpUtilities
{
    /// <summary>
    /// Extracts the client IP address from an HTTP request.
    /// Checks X-Forwarded-For, X-Real-IP headers (for proxied requests)
    /// and falls back to Connection.RemoteIpAddress.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <returns>The client IP address or empty string if not found</returns>
    public static string ExtractClientIP(HttpContext context)
    {
        // Check X-Forwarded-For header (for requests behind proxy/load balancer)
        // Format: "client, proxy1, proxy2"
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffValues))
        {
            var xff = xffValues.ToString();
            if (!string.IsNullOrWhiteSpace(xff))
            {
                // Take the first IP (the client)
                var ips = xff.Split(',');
                if (ips.Length > 0)
                {
                    var clientIP = ips[0].Trim();
                    // Validate it's a valid IP
                    if (IPAddress.TryParse(clientIP, out _))
                    {
                        return clientIP;
                    }
                }
            }
        }

        // Check X-Real-IP header (alternative proxy header)
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var xriValues))
        {
            var xri = xriValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(xri) && IPAddress.TryParse(xri, out _))
            {
                return xri;
            }
        }

        // Fallback to Connection.RemoteIpAddress (direct connection)
        var remoteIP = context.Connection.RemoteIpAddress;
        if (remoteIP != null)
        {
            // Handle IPv6 loopback (::1) and map to IPv4 if possible
            if (remoteIP.IsIPv4MappedToIPv6)
            {
                remoteIP = remoteIP.MapToIPv4();
            }
            return remoteIP.ToString();
        }

        return string.Empty;
    }
}
