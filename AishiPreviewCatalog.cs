using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
#if AISHI_SPT41
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
#else
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
#endif
using SPTarkov.Server.Core.Loaders;

namespace Aishi;

[Injectable]
#if AISHI_SPT41
public sealed class AishiPreviewCatalog(ModHelper mods, JsonUtil json, TemplateTable templates, GlobalTable globals, BundleLoader bundles)
#else
public sealed class AishiPreviewCatalog(ModHelper mods, JsonUtil json, DatabaseService database, BundleLoader bundles)
#endif
{
    private readonly object gate = new();
    private string? catalog;
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public string Get()
    {
        lock (gate) return catalog ??= Build();
    }

    private string Build()
    {
#if !AISHI_SPT41
        var templates = database.GetTemplates();
        var globals = database.GetGlobals();
#endif
        var root = Path.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        var owned = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(Path.GetDirectoryName(file)) is "CustomItems" or "Itens"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), ReadOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
            foreach (var entry in document.RootElement.EnumerateObject())
                if (MongoId.IsValidMongoId(entry.Name) && entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("itemTplToClone", out _) && templates.Items.ContainsKey(entry.Name))
                    owned.Add(entry.Name);
        }

        var nodes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        JsonObject Node(string id)
        {
            if (!nodes.TryGetValue(id, out var node))
                nodes[id] = node = JsonNode.Parse(json.Serialize(templates.Items[id])!)!.AsObject();
            return node;
        }
        var assets = bundles.GetBundles().GroupBy(bundle => bundle.FileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var assetStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Asset(string key, HashSet<string>? seen = null)
        {
            if (assetStamps.TryGetValue(key, out var cached)) return cached;
            if (!assets.TryGetValue(key, out var bundle)) return key;
            seen ??= new(StringComparer.OrdinalIgnoreCase);
            if (!seen.Add(key)) return key;
            var parts = new List<string> { key, bundle.Crc.ToString(CultureInfo.InvariantCulture) };
            foreach (var dependency in bundle.Bundle.DependencyKeys ?? []) parts.Add(Asset(dependency, seen));
            seen.Remove(key);
            return assetStamps[key] = Digest(string.Join("|", parts));
        }
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonNode? Visual(JsonNode? value) => value switch
        {
            JsonObject obj => new JsonObject(obj.Where(property => property.Key != "_id").Select(property => new KeyValuePair<string, JsonNode?>(property.Key, Visual(property.Value)))),
            JsonArray array => new JsonArray(array.Select(Visual).ToArray()),
            _ => value?.DeepClone()
        };
        string Stamp(string id)
        {
            if (stamps.TryGetValue(id, out var cached)) return cached;
            var properties = Node(id)["_props"]!.AsObject();
            var visual = new JsonObject();
            foreach (var property in properties.OrderBy(property => property.Key, StringComparer.Ordinal))
                if (property.Key is "Prefab" or "Width" or "Height" or "Slots" or "Grids" or "Foldable" or "HasHinge" or "HideEntrails" or "VisibleAmmoRangesString" or "UsePrefabSprite" || property.Key.StartsWith("ExtraSize", StringComparison.Ordinal))
                    visual[property.Key] = property.Key is "Slots" or "Grids" ? Visual(property.Value) : property.Value?.DeepClone();
            var path = properties["Prefab"]?["path"]?.GetValue<string>() ?? "";
            return stamps[id] = Digest(visual.ToJsonString() + Asset(path));
        }
        var jobs = new List<object>();
        foreach (var id in owned)
        {
            if (Node(id)["_props"]?["Prefab"]?["path"]?.GetValue<string>() is not { Length: > 0 }) continue;
            var flat = new JsonArray(new JsonObject { ["_id"] = id, ["_tpl"] = id });
            jobs.Add(new { key = id, template = id, stamp = Digest(Stamp(id)), flat = (JsonArray?)null, cacheKey = AishiPreviewHash.Calculate(flat, Node) });
        }
        var baseCount = jobs.Count;
        var presets = JsonNode.Parse(json.Serialize(globals.ItemPresets)!)!.AsObject();
        foreach (var entry in presets.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (entry.Value?["_items"] is not JsonArray flat || flat.Count == 0) continue;
            var first = flat.FirstOrDefault(item => item?["parentId"] == null);
            var id = first?["_tpl"]?.GetValue<string>();
            if (id == null || !owned.Contains(id)) continue;
            if (flat.Any(item => item?["_tpl"]?.GetValue<string>() is not { } tpl || !templates.Items.ContainsKey(tpl))) continue;
            var byParent = flat.Where(item => item?["parentId"] != null).GroupBy(item => item!["parentId"]!.GetValue<string>()).ToDictionary(group => group.Key, group => group.OrderBy(item => item!["slotId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray());
            string Tree(JsonNode item, HashSet<string> seen)
            {
                var instance = item["_id"]!.GetValue<string>();
                if (!seen.Add(instance)) throw new InvalidDataException("Cyclic Aishi preview preset: " + entry.Key);
                var tpl = item["_tpl"]!.GetValue<string>();
                var result = Stamp(tpl);
                if (item["upd"] is JsonObject upd)
                    foreach (var property in upd.OrderBy(property => property.Key, StringComparer.Ordinal))
                        if (property.Key is "Foldable" or "Togglable" or "StackObjectsCount") result += property.Key + property.Value?.ToJsonString();
                if (Node(tpl)["_props"]?["HideEntrails"]?.GetValue<bool>() != true && byParent.TryGetValue(instance, out var children))
                    foreach (var child in children) result += "|" + child!["slotId"]!.GetValue<string>() + ":" + Tree(child, seen);
                seen.Remove(instance);
                return result;
            }
            jobs.Add(new { key = entry.Key, template = id, stamp = Digest(Tree(first!, new())), flat, cacheKey = AishiPreviewHash.Calculate(flat, Node) });
        }
        return JsonSerializer.Serialize(new { version = 1, items = jobs.Skip(baseCount).Concat(jobs.Take(baseCount)) });
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

[Injectable]
#if AISHI_SPT41
public sealed class AishiPreviewRouter(JsonUtil json, AishiPreviewCatalog previews, AishiPreviewSeed seed) : StaticRouter(json, [new RouteAction("/aishi/previews/catalog", (url, info, session, output, token) => new ValueTask<object>(previews.Get())), new RouteAction("/aishi/previews/prepare", (url, info, session, output, token) => new ValueTask<object>(JsonSerializer.Serialize(new { ready = seed.Prepare() })))]);
#else
public sealed class AishiPreviewRouter(JsonUtil json, AishiPreviewCatalog previews, AishiPreviewSeed seed) : StaticRouter(json, [new RouteAction("/aishi/previews/catalog", (url, info, session, output) => new ValueTask<object>(previews.Get())), new RouteAction("/aishi/previews/prepare", (url, info, session, output) => new ValueTask<object>(JsonSerializer.Serialize(new { ready = seed.Prepare() })))]);
#endif
