using System.Text.Json;
using SPTarkov.DI.Annotations;

namespace Aishi;
[Injectable]
public sealed class AishiKteData
{
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, HashSet<string>> Profiles { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly string path;
    private readonly object sync = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    public AishiKteData() : this(Path.Combine(Path.GetDirectoryName(typeof(AishiKteData).Assembly.Location)!, "kte_data.json"))
    {
    }

    internal AishiKteData(string filename)
    {
        path = Path.GetFullPath(filename);
    }

    private Document Load() => File.Exists(path) ? JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Invalid kte_data.json.") : new Document();
    internal HashSet<string> Get(string profile)
    {
        lock (sync)
        {
            var document = Load();
            return document.Profiles.TryGetValue(profile, out var known) ? new HashSet<string>(known.Where(id => AishiAnomalyPolicy.Ids.Contains(id)), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        }
    }

    internal void Store(string profile, IEnumerable<string> revealed)
    {
        lock (sync)
        {
            var document = Load();
            var known = new HashSet<string>(revealed.Where(id => AishiAnomalyPolicy.Ids.Contains(id)), StringComparer.Ordinal);
            if (document.Profiles.TryGetValue(profile, out var prior))
                known.UnionWith(prior.Where(id => AishiAnomalyPolicy.Ids.Contains(id)));
            if (File.Exists(path) && prior != null && known.SetEquals(prior))
                return;
            document.Profiles[profile] = known;
            var temporary = path + ".writing-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options) + Environment.NewLine);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
