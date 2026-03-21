using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using TraceKit.Core.LLM;
using Xunit;

namespace TraceKit.Core.Tests.LLM;

/// <summary>
/// Tests for AzureOpenAiPolicy Azure OpenAI instrumentation.
/// Tests the core parsing/instrumentation logic via the internal AzureOpenAiInstrumentation
/// helper (same pattern as OpenAiHandler testing).
/// </summary>
public class AzureOpenAiPolicyTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = new();

    public AzureOpenAiPolicyTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "tracekit-llm",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
    }

    // --- Host Detection ---

    [Theory]
    [InlineData("myresource.openai.azure.com", true)]
    [InlineData("another.openai.azure.com", true)]
    [InlineData("api.openai.com", false)]
    [InlineData("example.com", false)]
    [InlineData("openai.azure.com", true)]
    public void IsAzureOpenAiHost_DetectsCorrectly(string host, bool expected)
    {
        Assert.Equal(expected, AzureOpenAiInstrumentation.IsAzureOpenAiHost(host));
    }

    // --- Deployment Name Extraction ---

    [Theory]
    [InlineData("/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-01", "gpt-4o")]
    [InlineData("/openai/deployments/my-gpt4/chat/completions", "my-gpt4")]
    [InlineData("/openai/deployments/claude-proxy/completions", "claude-proxy")]
    [InlineData("/v1/chat/completions", null)]
    public void ExtractDeploymentName_FromUrlPath(string path, string? expected)
    {
        Assert.Equal(expected, AzureOpenAiInstrumentation.ExtractDeploymentName(path));
    }

    // --- Non-streaming Response ---

    [Fact]
    public void NonStreamingResponse_SetsGenAiAttributes()
    {
        var requestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "Hello"}],
            "max_tokens": 100,
            "temperature": 0.7,
            "top_p": 0.9
        }
        """;

        var responseJson = """
        {
            "id": "chatcmpl-azure123",
            "model": "gpt-4o",
            "choices": [
                {
                    "index": 0,
                    "message": {"role": "assistant", "content": "Hi there!"},
                    "finish_reason": "stop"
                }
            ],
            "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 5,
                "total_tokens": 15
            }
        }
        """;

        var config = LlmConfig.Default;
        AzureOpenAiInstrumentation.InstrumentNonStreaming(
            config,
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            200,
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat gpt-4o", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("chat", activity.GetTagItem("gen_ai.operation.name")?.ToString());
        Assert.Equal("openai", activity.GetTagItem("gen_ai.system")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.request.model")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("chatcmpl-azure123", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("stop", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(5, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Model fallback to deployment name ---

    [Fact]
    public void DeploymentNameFallback_WhenNoModelInRequest()
    {
        var requestJson = """
        {
            "messages": [{"role": "user", "content": "Hello"}]
        }
        """;

        var responseJson = """
        {
            "id": "chatcmpl-dep",
            "model": "gpt-4o-2024-05-13",
            "choices": [{"index": 0, "message": {"role": "assistant", "content": "Hi"}, "finish_reason": "stop"}],
            "usage": {"prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7}
        }
        """;

        AzureOpenAiInstrumentation.InstrumentNonStreaming(
            LlmConfig.Default,
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            200,
            "/openai/deployments/my-gpt4o/chat/completions");

        Assert.Single(_activities);
        var activity = _activities[0];

        // Request model should fallback to deployment name
        Assert.Equal("my-gpt4o", activity.GetTagItem("gen_ai.request.model")?.ToString());
        // Response model from body
        Assert.Equal("gpt-4o-2024-05-13", activity.GetTagItem("gen_ai.response.model")?.ToString());
    }

    // --- Streaming Response ---

    [Fact]
    public void StreamingResponse_AccumulatesTokens()
    {
        var requestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "Hi"}],
            "stream": true
        }
        """;

        var sseLines = new[]
        {
            "data: {\"id\":\"chatcmpl-azstream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"},\"finish_reason\":null}]}",
            "",
            "data: {\"id\":\"chatcmpl-azstream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}",
            "",
            "data: {\"id\":\"chatcmpl-azstream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
            "",
            "data: {\"id\":\"chatcmpl-azstream1\",\"model\":\"gpt-4o\",\"choices\":[],\"usage\":{\"prompt_tokens\":8,\"completion_tokens\":4,\"total_tokens\":12}}",
            "",
            "data: [DONE]",
            ""
        };

        AzureOpenAiInstrumentation.InstrumentStreaming(
            LlmConfig.Default,
            Encoding.UTF8.GetBytes(requestJson),
            sseLines,
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat gpt-4o", activity.DisplayName);
        Assert.Equal("chatcmpl-azstream1", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("stop", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(8, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(4, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Tool Calls ---

    [Fact]
    public void ToolCalls_RecordedAsEvents()
    {
        var requestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "What is the weather?"}]
        }
        """;

        var responseJson = """
        {
            "id": "chatcmpl-aztools",
            "model": "gpt-4o",
            "choices": [
                {
                    "index": 0,
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_az123",
                                "type": "function",
                                "function": {
                                    "name": "get_weather",
                                    "arguments": "{\"city\":\"Seattle\"}"
                                }
                            }
                        ]
                    },
                    "finish_reason": "tool_calls"
                }
            ],
            "usage": {"prompt_tokens": 15, "completion_tokens": 8, "total_tokens": 23}
        }
        """;

        AzureOpenAiInstrumentation.InstrumentNonStreaming(
            LlmConfig.Default,
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            200,
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Single(_activities);
        var activity = _activities[0];

        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("gen_ai.tool.call", events[0].Name);

        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("get_weather", tags["gen_ai.tool.name"]);
        Assert.Equal("call_az123", tags["gen_ai.tool.call.id"]);
        Assert.Contains("Seattle", tags["gen_ai.tool.call.arguments"]?.ToString());
    }

    // --- Content Capture ---

    [Fact]
    public void ContentCapture_WhenEnabled_SetsInputAndOutput()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", "true");

            var requestJson = """
            {
                "model": "gpt-4o",
                "messages": [{"role": "user", "content": "Hello there"}]
            }
            """;

            var responseJson = """
            {
                "id": "chatcmpl-cap",
                "model": "gpt-4o",
                "choices": [
                    {
                        "index": 0,
                        "message": {"role": "assistant", "content": "Hi!"},
                        "finish_reason": "stop"
                    }
                ],
                "usage": {"prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7}
            }
            """;

            AzureOpenAiInstrumentation.InstrumentNonStreaming(
                LlmConfig.Default,
                Encoding.UTF8.GetBytes(requestJson),
                Encoding.UTF8.GetBytes(responseJson),
                200,
                "/openai/deployments/gpt-4o/chat/completions");

            Assert.Single(_activities);
            var activity = _activities[0];

            Assert.NotNull(activity.GetTagItem("gen_ai.input.messages"));
            Assert.NotNull(activity.GetTagItem("gen_ai.output.messages"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    // --- Error Status ---

    [Fact]
    public void ErrorStatusCode_SetsHttpStatusCode()
    {
        var requestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "Hello"}]
        }
        """;

        AzureOpenAiInstrumentation.InstrumentNonStreaming(
            LlmConfig.Default,
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes("{}"),
            429,
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Single(_activities);
        var activity = _activities[0];
        Assert.Equal(429, activity.GetTagItem("http.status_code"));
    }

    // --- Disabled Config ---

    [Fact]
    public void DisabledConfig_NoSpanCreated()
    {
        var config = new LlmConfig { Enabled = false };
        var requestJson = """{"model":"gpt-4o","messages":[]}""";

        var result = AzureOpenAiInstrumentation.TryStartInstrumentation(
            config,
            Encoding.UTF8.GetBytes(requestJson),
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Null(result);
        Assert.Empty(_activities);
    }

    [Fact]
    public void OpenAIDisabled_NoSpanCreated()
    {
        var config = new LlmConfig { OpenAI = false };
        var requestJson = """{"model":"gpt-4o","messages":[]}""";

        var result = AzureOpenAiInstrumentation.TryStartInstrumentation(
            config,
            Encoding.UTF8.GetBytes(requestJson),
            "/openai/deployments/gpt-4o/chat/completions");

        Assert.Null(result);
        Assert.Empty(_activities);
    }

    // --- Stream options injection ---

    [Fact]
    public void InjectStreamUsage_AddsIncludeUsage()
    {
        var requestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "test"}],
            "stream": true
        }
        """;

        var result = AzureOpenAiInstrumentation.InjectStreamUsage(
            Encoding.UTF8.GetBytes(requestJson));

        var resultStr = Encoding.UTF8.GetString(result);
        Assert.Contains("include_usage", resultStr);
    }
}
