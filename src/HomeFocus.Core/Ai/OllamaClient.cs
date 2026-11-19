using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeFocus.Core.Ai;

/// <summary>Picks a category for an app or website.</summary>
public interface IClassifier
{
    /// <returns>One of <paramref name="categories"/>, or null when the model gave no usable answer.</returns>
    Task<string?> ClassifyAsync(string position, IReadOnlyList<string> categories, ActivityKind kind, string key, string displayName,
        CancellationToken ct);
}

/// <summary>
/// Talks to an Ollama server (by default the local one on 127.0.0.1:11434). Only the app/website name,
/// the category names and the user's job title are sent; the answer is constrained to the category list.
/// </summary>
public sealed class OllamaClient(HttpClient http, string baseUrl, string model) : IClassifier
{
    public const string DefaultUrl = "http://127.0.0.1:11434";

    private readonly Uri _base = Normalize(baseUrl);

    /// <summary>Accepts only http(s) URLs; throws <see cref="ArgumentException"/> otherwise.</summary>
    public static Uri Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new ArgumentException("Ollama URL must start with http:// or https://", nameof(url));
        return u;
    }

    /// <summary>True when the server is this computer (nothing leaves the PC).</summary>
    public static bool IsLocal(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host is "localhost" or "127.0.0.1" or "[::1]");

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync(new Uri(_base, "api/tags"), ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(ct);
        return json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().OrderBy(n => n).ToList() ?? [];
    }

    public async Task<string?> ClassifyAsync(string position, IReadOnlyList<string> categories, ActivityKind kind, string key,
        string displayName, CancellationToken ct)
    {
        var body = BuildRequest(model, position, categories, kind, key, displayName);
        // Reasoning models (qwen3, deepseek-r1…) answer ~5× faster without thinking; older servers may reject the flag.
        body["think"] = false;
        var res = await Post(body, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            res.Dispose();
            body.Remove("think");
            res = await Post(body, ct);
        }
        using var _ = res;
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonNode>(ct);
        return ParseAnswer(json?["message"]?["content"]?.GetValue<string>(), categories);
    }

    private Task<HttpResponseMessage> Post(JsonObject body, CancellationToken ct) =>
        http.PostAsync(new Uri(_base, "api/chat"), new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);

    /// <summary>Chat request with a JSON schema that only allows the given category names. Public for tests.</summary>
    public static JsonObject BuildRequest(string model, string position, IReadOnlyList<string> categories, ActivityKind kind,
        string key, string displayName)
    {
        var item = kind == ActivityKind.Web
            ? $"Website: {key}"
            : $"Application: {displayName} (process {key})";
        var system =
            "You sort how a person spends time at their computer into categories. " +
            $"The person's job is: {position.Trim()}. " +
            "Pick the single best category for the item. Choose the work category only when the item is plausibly part of this " +
            "person's job; entertainment, games, shopping and social media are not work unless the job is about them. " +
            $"Categories: {string.Join(", ", categories)}. Answer with JSON only.";
        return new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["format"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["category"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(categories.Select(c => (JsonNode)c!).ToArray()) },
                },
                ["required"] = new JsonArray("category"),
            },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = item }),
        };
    }

    /// <summary>Reads {"category": "..."} and returns it only if it is one of the categories. Public for tests.</summary>
    public static string? ParseAnswer(string? content, IReadOnlyList<string> categories)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            var name = JsonNode.Parse(content)?["category"]?.GetValue<string>()?.Trim();
            return categories.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
