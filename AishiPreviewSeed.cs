using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
#if AISHI_SPT41
using SPTarkov.Server.Core.Helpers.Server;
#else
using SPTarkov.Server.Core.Helpers;
#endif

namespace Aishi;

#if AISHI_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 3)]
#else
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 3)]
#endif
public sealed class AishiPreviewSeed(ModHelper mods, ILogger<AishiPreviewSeed> logger) : IOnLoad
{
    private static readonly object Gate = new();

    private sealed class Manifest
    {
        public int Version { get; set; }
        public string Key { get; set; } = "";
        public Entry[] Entries { get; set; } = [];
        public Dictionary<int, int> Aliases { get; set; } = [];
    }

    private sealed class Entry
    {
        public int Hash { get; set; }
        public string Stamp { get; set; } = "";
        public string Image { get; set; } = "";
    }

#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if AISHI_SPT41
        cancellationToken.ThrowIfCancellationRequested();
#endif
        Prepare();
        return Task.CompletedTask;
    }

    public bool Prepare()
    {
        lock (Gate)
        {
            var source = Path.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db", "PreviewCacheSeed");
            if (!File.Exists(Path.Combine(source, "manifest.json"))) return false;
            try
            {
                Import(source, Path.Combine(AppContext.BaseDirectory, "user", "sptappdata", "live"));
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                logger.LogWarning(error, "[Aishi] Prepared item previews could not be imported. Missing previews will be generated in the main menu.");
                return false;
            }
        }
    }

    internal static void Import(string source, string cache)
    {
        var manifestPath = Path.Combine(source, "manifest.json");
        if (!File.Exists(manifestPath)) return;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid Aishi preview seed.");
        if (manifest.Version != 1 || manifest.Key == null || manifest.Key.Length != 64 || manifest.Entries == null || manifest.Aliases == null
            || manifest.Entries.Length == 0 || manifest.Entries.Any(entry => entry == null || entry.Image == null || entry.Image.Length != 64 || !entry.Image.All(Uri.IsHexDigit) || string.IsNullOrEmpty(entry.Stamp))
            || manifest.Entries.Select(entry => entry.Hash).Distinct().Count() != manifest.Entries.Length
            || manifest.Aliases.Values.Except(manifest.Entries.Select(entry => entry.Hash)).Any())
            throw new InvalidDataException("Invalid Aishi preview seed.");
        var indexPath = Path.Combine(cache, "index.json");
        var statePath = Path.Combine(cache, "aishi-previews.json");
        var index = File.Exists(indexPath) ? JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(indexPath)) ?? throw new InvalidDataException("Invalid native icon index.") : [];
        var state = File.Exists(statePath) ? JsonNode.Parse(File.ReadAllText(statePath)) as JsonObject ?? throw new InvalidDataException("Invalid Aishi preview state.") : new JsonObject();
        var stamps = state["stamps"]?.Deserialize<Dictionary<int, string>>() ?? [];
        var aliases = state["aliases"]?.Deserialize<Dictionary<int, int>>() ?? [];
        Directory.CreateDirectory(cache);
        bool Exists(int hash) => index.TryGetValue(hash, out var file) && File.Exists(Path.Combine(cache, file + ".png"));
        var next = Math.Max(index.Values.DefaultIfEmpty(0).Max(), Directory.EnumerateFiles(cache, "*.png")
            .Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out var number) ? number : 0).DefaultIfEmpty(0).Max());
        var updated = state["seed"]?.GetValue<string>() != manifest.Key;
        var changed = false;
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (Exists(entry.Hash) && (stamps.GetValueOrDefault(entry.Hash) == entry.Stamp || !updated)) continue;
            var image = Path.Combine(source, entry.Image + ".png");
            if (validated.Add(entry.Image))
            {
                using var stream = File.OpenRead(image);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Image, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Damaged Aishi preview seed image.");
            }
            var destination = Path.Combine(cache, ++next + ".png");
            var temporary = destination + ".aishi-tmp";
            try { File.Copy(image, temporary, true); File.Move(temporary, destination); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            index[entry.Hash] = next;
            stamps[entry.Hash] = entry.Stamp;
            changed = true;
        }
        foreach (var alias in manifest.Aliases)
        {
            if (!index.TryGetValue(alias.Value, out var file) || index.ContainsKey(alias.Key) && aliases.GetValueOrDefault(alias.Key) != alias.Value) continue;
            if (index.GetValueOrDefault(alias.Key) == file && aliases.GetValueOrDefault(alias.Key) == alias.Value) continue;
            index[alias.Key] = file;
            aliases[alias.Key] = alias.Value;
            changed = true;
        }
        if (!changed && !updated) return;
        state["stamps"] = JsonSerializer.SerializeToNode(stamps);
        state["aliases"] = JsonSerializer.SerializeToNode(aliases);
        state["seed"] = manifest.Key;
        Write(indexPath, JsonSerializer.Serialize(index));
        Write(statePath, state.ToJsonString());
    }

    private static void Write(string path, string content)
    {
        var temporary = path + ".aishi-tmp";
        try { File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
