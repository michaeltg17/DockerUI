using System.Text.Json;

namespace Api.Features.Shortcuts;

/// <summary>
/// Persists shortcuts to a JSON file in the app's content root. Reads and writes are
/// serialized so concurrent requests never observe a torn file.
/// </summary>
public sealed partial class ShortcutStore
{
    const string FileName = "shortcuts.json";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly Lock _gate = new();
    readonly string _path;
    readonly ILogger _logger;

    public ShortcutStore(IWebHostEnvironment environment, ILogger<ShortcutStore> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _path = Path.Combine(environment.ContentRootPath, FileName);
        _logger = logger;
    }

    public IReadOnlyList<Shortcut> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<Shortcut>>(File.ReadAllText(_path), Json) ?? [];
            }
            catch (JsonException ex)
            {
                LogParseFailed(_logger, _path, ex);
                return [];
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The shortcuts file '{Path}' could not be parsed; ignoring it.")]
    static partial void LogParseFailed(ILogger logger, string path, Exception? exception);

    public IReadOnlyList<Shortcut> Save(IEnumerable<Shortcut> shortcuts)
    {
        var list = shortcuts.ToList();

        lock (_gate)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(list, Json));
        }

        return list;
    }
}
