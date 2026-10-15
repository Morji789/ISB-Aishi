using System.Text.Json;

namespace Aishi;
internal static class AishiLocaleData
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    internal static bool LocaleDiagnosticsEnabled(string root)
    {
        var path = Path.Combine(root, "config.jsonc");
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.TryGetProperty("logging", out var logging))
                return logging.ValueKind == JsonValueKind.Object && logging.TryGetProperty("localeDiagnostics", out var value) && value.ValueKind == JsonValueKind.True;
        }

        var legacy = Path.Combine(root, "logging.jsonc");
        if (!File.Exists(legacy))
            return false;
        using var previous = JsonDocument.Parse(File.ReadAllText(legacy), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return previous.RootElement.TryGetProperty("localeDiagnostics", out var old) && old.ValueKind == JsonValueKind.True;
    }

    internal static Dictionary<string, Dictionary<string, string>> ReadDefaults(string root)
    {
        var path = Path.Combine(root, "db", "CustomLocales", "en.json");
        var defaults = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options) ?? new() : new Dictionary<string, string>();
        var presets = Path.Combine(root, "db", "CustomWeaponPresets");
        if (Directory.Exists(presets))
            foreach (var file in Directory.EnumerateFiles(presets, "*.json", SearchOption.AllDirectories))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var entry in document.RootElement.EnumerateObject())
                    if (entry.Value.TryGetProperty("_name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                        defaults.TryAdd(entry.Name, name.GetString()!);
            }

        var items = Path.Combine(root, "db", "CustomItems");
        if (Directory.Exists(items))
            foreach (var file in Directory.EnumerateFiles(items, "*.json", SearchOption.AllDirectories))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    if (!entry.Value.TryGetProperty("weaponPresets", out var embedded) || embedded.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var preset in embedded.EnumerateArray())
                        if (preset.TryGetProperty("_id", out var id) && id.ValueKind == JsonValueKind.String && preset.TryGetProperty("_name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                            defaults.TryAdd(id.GetString()!, name.GetString()!);
                }
            }

        var localized = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = defaults
        };
        if (Directory.Exists(items))
        foreach (var anomalies in Directory.EnumerateFiles(items, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(anomalies));
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Value.TryGetProperty("locales", out var locales))
                    foreach (var language in locales.EnumerateObject())
                        if (language.Value.TryGetProperty("revealedDescription", out var description) && description.ValueKind == JsonValueKind.String)
                        {
                            if (!localized.TryGetValue(language.Name, out var target))
                                localized[language.Name] = target = new(defaults);
                            target[entry.Name + " RevealedDescription"] = description.GetString()!;
                        }
        }

        return localized;
    }

    internal static Dictionary<string, string>? ReadOverride(string root, string language)
    {
        if (string.Equals(language, "template", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.Combine(root, "db", "CustomLocales", language + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options) : null;
    }

    internal static void Apply(IDictionary<string, string> locale, Dictionary<string, Dictionary<string, string>> defaults, string language, Dictionary<string, string>? custom)
    {
        if (defaults.TryGetValue(language, out var selected) || defaults.TryGetValue("en", out selected))
            foreach (var entry in selected)
                if (!locale.TryGetValue(entry.Key, out var value) || string.IsNullOrWhiteSpace(value) || value == entry.Key)
                    locale[entry.Key] = entry.Value;
        if (custom != null)
            foreach (var entry in custom)
                locale[entry.Key] = entry.Value;
    }
}
