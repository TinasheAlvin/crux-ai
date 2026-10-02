using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;
using Microsoft.Extensions.Options;

namespace VhonaAI.Infrastructure.Why;

public sealed class AzureOpenAIIntentClassifier : IAzureOpenAIIntentClassifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly AzureOpenAIOptions _options;

    public AzureOpenAIIntentClassifier(IOptions<AzureOpenAIOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<HealthMetricKind?> TryClassifyAsync(string question, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        try
        {
            var url =
                $"{_options.Endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(_options.DeploymentName)}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = "Classify the user question as one KPI: Revenue, Expenses, Profit, Cash, or none. Reply with JSON only: {\"metric\":\"Revenue\"} or {\"metric\":null}. Do not include numbers or explanations."
                        },
                        new { role = "user", content = question }
                    },
                    temperature = 0,
                    max_tokens = 40
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            return ParseMetric(content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static HealthMetricKind? ParseMetric(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var json = content.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return WhyIntentParser.ParseMetric(json);
        }

        json = json[start..(end + 1)];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("metric", out var metric) || metric.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            var value = metric.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Enum.TryParse<HealthMetricKind>(value, ignoreCase: true, out var kind)
                ? kind
                : WhyIntentParser.ParseMetric(value);
        }
        catch (JsonException)
        {
            return WhyIntentParser.ParseMetric(json);
        }
    }
}
