using System.Collections.Concurrent;
using System.Text.Json;
using E2E.Environments;

namespace E2E.Playwright;

/// <summary>
/// Writes the JavaScript coverage reports one e2e session collected (the
/// driver's Page-channel 'stopJSCoverage' answers, { entries: [ ScriptCoverage,
/// ... ] }) to 'e2e/coverage/&lt;scenario&gt;/context-&lt;n&gt;.v8.json', flattened
/// into a single { result: [ ScriptCoverage, ... ] } payload. The e2e suite's
/// convert step (e2e/coverage/convert.mjs) merges every dump of a scenario and
/// remaps it to ui/src through the e2e image's source maps.
/// </summary>
internal static class JsCoverageDump
{
    static readonly ConcurrentDictionary<string, int> ScenarioCounters = new(StringComparer.OrdinalIgnoreCase);

    public static void Write(string scenarioName, IReadOnlyList<JsonElement> stopResponses)
    {
        var directory = Paths.CombineE2e("coverage", scenarioName);
        Directory.CreateDirectory(directory);

        var number = ScenarioCounters.AddOrUpdate(scenarioName, 1, static (_, current) => current + 1);
        var path = Path.Combine(directory, $"context-{number}.v8.json");

        using var file = File.Create(path);
        using var writer = new Utf8JsonWriter(file);

        writer.WriteStartObject();
        writer.WritePropertyName("result");
        writer.WriteStartArray();

        foreach (var stopResponse in stopResponses)
        {
            // Each stopJSCoverage answer carries its page's ScriptCoverage entries.
            if (stopResponse.ValueKind == JsonValueKind.Object &&
                stopResponse.TryGetProperty("entries", out var pageCoverage) &&
                pageCoverage.ValueKind == JsonValueKind.Array)
            {
                foreach (var scriptCoverage in pageCoverage.EnumerateArray())
                    scriptCoverage.WriteTo(writer);
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
