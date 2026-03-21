using System.Diagnostics;
using System.Net;
using System.Text;
using TraceKit.Core.LLM;
using Xunit;

namespace TraceKit.Core.Tests.LLM;

/// <summary>
/// Tests for TracekitLlmHandler and OpenAiHandler.
/// Uses a MockInnerHandler to return pre-built responses and an ActivityListener
/// to capture Activities for assertion.
/// </summary>
public class OpenAiHandlerTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = new();

    public OpenAiHandlerTests()
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

    // --- Passthrough Tests ---

    [Fact]
    public async Task NonLlmRequest_PassesThrough()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var response = await client.PostAsync("https://example.com/api",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_activities);
    }

    [Fact]
    public async Task GetRequest_PassesThrough()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.openai.com/v1/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_activities);
    }

    [Fact]
    public async Task DisabledConfig_PassesThrough()
    {
        var config = new LlmConfig { Enabled = false };
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracekitLlmHandler(config, mock);
        var client = new HttpClient(handler);

        var response = await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(OpenAiRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_activities);
    }

    // --- Non-streaming Response ---

    [Fact]
    public async Task NonStreamingResponse_SetsGenAiAttributes()
    {
        var responseJson = """
        {
            "id": "chatcmpl-abc123",
            "model": "gpt-4o",
            "choices": [
                {
                    "index": 0,
                    "message": {"role": "assistant", "content": "Hello!"},
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

        var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(OpenAiRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat gpt-4o", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("chat", activity.GetTagItem("gen_ai.operation.name")?.ToString());
        Assert.Equal("openai", activity.GetTagItem("gen_ai.system")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.request.model")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("chatcmpl-abc123", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("stop", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(5, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Streaming Response ---

    [Fact]
    public async Task StreamingResponse_AccumulatesTokens()
    {
        var sseData = new StringBuilder();
        sseData.AppendLine("data: {\"id\":\"chatcmpl-stream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"},\"finish_reason\":null}]}");
        sseData.AppendLine();
        sseData.AppendLine("data: {\"id\":\"chatcmpl-stream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}");
        sseData.AppendLine();
        sseData.AppendLine("data: {\"id\":\"chatcmpl-stream1\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}");
        sseData.AppendLine();
        sseData.AppendLine("data: {\"id\":\"chatcmpl-stream1\",\"model\":\"gpt-4o\",\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3,\"total_tokens\":15}}");
        sseData.AppendLine();
        sseData.AppendLine("data: [DONE]");
        sseData.AppendLine();

        var streamRequestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "Hi"}],
            "stream": true
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(
                new MemoryStream(Encoding.UTF8.GetBytes(sseData.ToString())))
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        var mock = new MockInnerHandler(response);
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var result = await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(streamRequestJson, Encoding.UTF8, "application/json"));

        // Consume the stream to trigger SSE parsing
        var outputStream = new MemoryStream();
        await result.Content.CopyToAsync(outputStream);

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat gpt-4o", activity.DisplayName);
        Assert.Equal("chatcmpl-stream1", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("gpt-4o", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("stop", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(12, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(3, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Stream injects include_usage ---

    [Fact]
    public async Task StreamingRequest_InjectsIncludeUsage()
    {
        var streamRequestJson = """
        {
            "model": "gpt-4o",
            "messages": [{"role": "user", "content": "test"}],
            "stream": true
        }
        """;

        byte[]? capturedBody = null;
        var mock = new MockInnerHandler(req =>
        {
            capturedBody = req.Content?.ReadAsByteArrayAsync().Result;
            var sseData = "data: {\"id\":\"x\",\"model\":\"gpt-4o\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}\n\ndata: [DONE]\n\n";
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new MemoryStream(Encoding.UTF8.GetBytes(sseData)))
            };
            resp.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return resp;
        });

        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var result = await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(streamRequestJson, Encoding.UTF8, "application/json"));

        // Consume stream
        await result.Content.CopyToAsync(new MemoryStream());

        Assert.NotNull(capturedBody);
        var bodyStr = Encoding.UTF8.GetString(capturedBody!);
        Assert.Contains("include_usage", bodyStr);
    }

    // --- Tool Calls ---

    [Fact]
    public async Task ToolCalls_RecordedAsEvents()
    {
        var responseJson = """
        {
            "id": "chatcmpl-tools",
            "model": "gpt-4o",
            "choices": [
                {
                    "index": 0,
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_abc123",
                                "type": "function",
                                "function": {
                                    "name": "get_weather",
                                    "arguments": "{\"city\":\"London\"}"
                                }
                            }
                        ]
                    },
                    "finish_reason": "tool_calls"
                }
            ],
            "usage": { "prompt_tokens": 20, "completion_tokens": 10, "total_tokens": 30 }
        }
        """;

        var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(OpenAiRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];

        // Check tool call event
        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("gen_ai.tool.call", events[0].Name);

        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("get_weather", tags["gen_ai.tool.name"]);
        Assert.Equal("call_abc123", tags["gen_ai.tool.call.id"]);
        Assert.Contains("London", tags["gen_ai.tool.call.arguments"]?.ToString());
    }

    // --- Content Capture ---

    [Fact]
    public async Task ContentCapture_WhenEnabled_SetsInputAndOutput()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", "true");

            var responseJson = """
            {
                "id": "chatcmpl-cap",
                "model": "gpt-4o",
                "choices": [
                    {
                        "index": 0,
                        "message": {"role": "assistant", "content": "Hello there!"},
                        "finish_reason": "stop"
                    }
                ],
                "usage": { "prompt_tokens": 5, "completion_tokens": 3, "total_tokens": 8 }
            }
            """;

            var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
            var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
            var client = new HttpClient(handler);

            await client.PostAsync("https://api.openai.com/v1/chat/completions",
                new StringContent(OpenAiRequestJson(), Encoding.UTF8, "application/json"));

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

    // --- Error Status Code ---

    [Fact]
    public async Task ErrorStatusCode_SetsHttpStatusCodeAttribute()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(OpenAiRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];
        Assert.Equal(429, activity.GetTagItem("http.status_code"));
    }

    // --- Anthropic passthrough ---

    [Fact]
    public async Task AnthropicHost_CreatesSpan()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"msg_1","type":"message","model":"claude-sonnet-4-20250514","content":[],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":5}}""",
                Encoding.UTF8, "application/json")
        });
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent("""{"model":"claude-sonnet-4-20250514","messages":[]}""",
                Encoding.UTF8, "application/json"));

        // Anthropic handler wired in Plan 02 -- span created
        Assert.Single(_activities);
    }

    // --- Helpers ---

    private static string OpenAiRequestJson() => """
        {
            "model": "gpt-4o",
            "messages": [
                {"role": "user", "content": "Hello"}
            ]
        }
        """;

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// Mock DelegatingHandler inner handler for testing.
    /// </summary>
    private class MockInnerHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockInnerHandler(HttpResponseMessage response)
        {
            _handler = _ => response;
        }

        public MockInnerHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
