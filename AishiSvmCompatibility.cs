using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Aishi;
internal static class AishiSvmPolicy
{
    private static readonly Lazy<JsonObject?> ActivePreset = new(ReadPreset);
    internal static JsonObject? Preset => ActivePreset.Value;
    internal static bool FreeLabs => Preset?["Raids"]?["EnableRaids"]?.GetValue<bool>() == true && Preset?["Raids"]?["Removelabkey"]?.GetValue<bool>() == true;

    private static JsonObject? ReadPreset()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ServerValueModifier");
        if (assembly == null || string.IsNullOrEmpty(assembly.Location))
            return null;
        var root = Path.GetDirectoryName(assembly.Location)!;
        var loader = Path.Combine(root, "Loader", "loader.json");
        if (!File.Exists(loader))
            return null;
        var name = JsonNode.Parse(File.ReadAllText(loader))["CurrentlySelectedPreset"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            return null;
        var file = Path.Combine(root, "Presets", name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null;
    }

    internal static string? AmmoGroup(string name)
    {
        var groups = new (string Name, string[] Calibers)[]
        {
            ("LargeCaliberRound", new[] { "127x55", "127x99", "86x70", "BMG" }),
            ("ShotgunRound", new[] { "12x70", "20x70", "23x75" }),
            ("PistolRound", new[] { "9x19", "9x18pm", "9x21", "762x25tt", "46x30", "57x28", "1143x23", "127x33", "9x33r", "10MM", "40SW", "357SIG", "9MM", "45ACP", "50AE", "380AUTO" }),
            ("MarksmanRound", new[] { "762x51", "68x51", "762х54R", "762x54", "277" }),
            ("RifleRound", new[] { "762x39", "545x39", "556x45", "9x39", "366", "762x35", "300blk", "ATL15", "GRENDEL", "50WLF", "KURZ" })
        };
        return groups.FirstOrDefault(g => g.Calibers.Any(c => name.Contains(c, StringComparison.OrdinalIgnoreCase))).Name;
    }

    internal static Dictionary<string, JsonNode> Changes(JsonObject options, JsonObject original, string parent, string caliber)
    {
        var result = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        if (options["EnableItems"]?.GetValue<bool>() != true)
            return result;
        foreach (var pair in new[]
        {
            ("Weight", "WeightChanger"),
            ("FragmentationChance", "FragmentMult"),
            ("HeatFactorByShot", "HeatFactor"),
            ("BaseMalfunctionChance", "MalfunctChanceMult"),
            ("MalfunctionChance", "MalfunctChanceMult"),
            ("LootExperience", "LootExp"),
            ("ExamineExperience", "ExamineExp")
        }

        )
        {
            if (original[pair.Item1] == null || options[pair.Item2] == null)
                continue;
            double value = original[pair.Item1]!.GetValue<double>() * options[pair.Item2]!.GetValue<double>();
            if (pair.Item1.EndsWith("Experience"))
                value = Math.Max(1, (int)value);
            if (pair.Item1 == "BaseMalfunctionChance")
                value = Math.Round(value, 4);
            result[pair.Item1] = JsonValue.Create(value);
        }

        if (original["HeatFactor"]?.GetValue<double>()is double heat && heat > 1 && options["HeatFactor"] != null)
            result["HeatFactor"] = JsonValue.Create(Math.Round(1 + (heat - 1) * options["HeatFactor"]!.GetValue<double>(), 4));
        if (original["ExamineTime"] != null && options["ExamineTime"] != null)
            result["ExamineTime"] = options["ExamineTime"]!;
        if (parent == "5485a8684bdc2da71d8b4567" && options["AmmoSwitch"]?.GetValue<bool>() == true)
        {
            var group = AmmoGroup(caliber);
            if (group != null && options["AmmoStacks"]?[group]?.GetValue<int>()is int size && size > 0)
                result["StackMaxSize"] = JsonValue.Create(size);
        }

        if (options["NoGearPenalty"]?.GetValue<bool>() == true)
            foreach (var property in new[]
            {
                "MousePenalty",
                "WeaponErgonomicPenalty",
                "SpeedPenaltyPercent"
            }

            )
                result[property] = JsonValue.Create(0);
        if (options["WeaponHeatOff"]?.GetValue<bool>() == true)
            result["AllowOverheat"] = JsonValue.Create(false);
        if (options["EquipRigsWithArmors"]?.GetValue<bool>() == true)
            result["BlocksArmorVest"] = JsonValue.Create(false);
        if (options["RaidDrop"]?.GetValue<bool>() == true)
            result["DiscardLimit"] = JsonValue.Create(-1);
        bool key = new[]
        {
            "5c99f98d86f7745c314214b3",
            "5c164d2286f774194c5e69fa",
            "543be5e94bdc2df1348b4568"
        }.Contains(parent);
        if (options["ExamineKeys"]?.GetValue<bool>() == true && (key || options["AllExaminedItems"]?.GetValue<bool>() == true))
            result["ExaminedByDefault"] = JsonValue.Create(true);
        return result;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1900)]
public sealed class AishiSvmCompatibility(TemplateTable templates, ModHelper modHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var options = AishiSvmPolicy.Preset?["Items"] as JsonObject;
        if (options?["EnableItems"]?.GetValue<bool>() != true)
            return Task.CompletedTask;
        var db = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        foreach (var file in Directory.EnumerateFiles(db, "*.json", SearchOption.AllDirectories).Where(p => p.Contains("CustomItems", StringComparison.OrdinalIgnoreCase) || p.Contains("Itens", StringComparison.OrdinalIgnoreCase)))
        {
            var document = JsonNode.Parse(File.ReadAllText(file));
            if (document is not JsonObject entries)
                continue;
            foreach (var entry in entries)
            {
                if (entry.Value is not JsonObject definition || definition["itemTplToClone"] == null || !templates.Items.TryGetValue(entry.Key, out var item) || item.Properties == null)
                    continue;
                var properties = item.Properties;
                string clone = definition["itemTplToClone"]!.GetValue<string>()!;
                string caliber = properties.Caliber ?? (templates.Items.TryGetValue(clone, out var source) ? source.Name : item.Name) ?? "";
                var original = definition["overrideProperties"] as JsonObject ?? new JsonObject();
                foreach (var change in AishiSvmPolicy.Changes(options, original, item.Parent.ToString(), caliber))
                {
                    var member = properties.GetType().GetProperty(change.Key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    if (member?.CanWrite == true && member.GetValue(properties) != null)
                        member.SetValue(properties, change.Value.Deserialize(member.PropertyType));
                }

                if (definition["handbookPriceRoubles"] != null && options["ItemPriceMult"] != null)
                {
                    var handbook = templates.Handbook.Items.FirstOrDefault(h => h.Id == item.Id);
                    if (handbook != null)
                        handbook.Price = definition["handbookPriceRoubles"]!.GetValue<double>() * options["ItemPriceMult"]!.GetValue<double>();
                }
            }
        }

        return Task.CompletedTask;
    }
}
