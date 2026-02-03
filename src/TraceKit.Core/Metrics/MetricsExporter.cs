using System.Net.Http.Json;
using System.Text.Json;

namespace TraceKit.Core.Metrics;

/// <summary>
/// Exports metrics to TraceKit in OTLP (OpenTelemetry Protocol) format.
/// </summary>
internal sealed class MetricsExporter
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _serviceName;
    private readonly HttpClient _httpClient;

    public MetricsExporter(string endpoint, string apiKey, string serviceName)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _serviceName = serviceName;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public void Export(List<MetricDataPoint> dataPoints)
    {
        if (dataPoints.Count == 0) return;

        var payload = ToOTLP(dataPoints);
        var json = JsonSerializer.Serialize(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-API-Key", _apiKey);

        try
        {
            var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Metrics export failed: HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Metrics export error: {ex.Message}");
        }
    }

    private object ToOTLP(List<MetricDataPoint> dataPoints)
    {
        // Group by name and type
        var grouped = dataPoints
            .GroupBy(dp => $"{dp.Name}:{dp.Type}")
            .ToList();

        var metrics = new List<object>();

        foreach (var group in grouped)
        {
            var parts = group.Key.Split(':');
            var name = parts[0];
            var type = parts[1];

            var otlpDataPoints = group.Select(dp => new
            {
                attributes = dp.Tags.Select(kvp => new
                {
                    key = kvp.Key,
                    value = new { stringValue = kvp.Value }
                }).ToArray(),
                timeUnixNano = dp.TimestampNanos,
                asDouble = dp.Value
            }).ToArray();

            object metric;
            if (type == "counter")
            {
                metric = new
                {
                    name,
                    sum = new
                    {
                        dataPoints = otlpDataPoints,
                        aggregationTemporality = 2, // DELTA
                        isMonotonic = true
                    }
                };
            }
            else // gauge or histogram
            {
                metric = new
                {
                    name,
                    gauge = new
                    {
                        dataPoints = otlpDataPoints
                    }
                };
            }

            metrics.Add(metric);
        }

        return new
        {
            resourceMetrics = new[]
            {
                new
                {
                    resource = new
                    {
                        attributes = new[]
                        {
                            new
                            {
                                key = "service.name",
                                value = new { stringValue = _serviceName }
                            }
                        }
                    },
                    scopeMetrics = new[]
                    {
                        new
                        {
                            scope = new { name = "tracekit" },
                            metrics
                        }
                    }
                }
            }
        };
    }
}
