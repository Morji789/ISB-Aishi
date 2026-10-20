using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils.Cloners;
#if AISHI_SPT41
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
#else
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
#endif

namespace Aishi;

[Injectable(TypePriority = int.MaxValue - 10)]
#if AISHI_SPT41
public sealed class AishiCosmeticSlots(ModHelper mods, ICloner cloner, TemplateTable templates) : IOnLoad
#else
public sealed class AishiCosmeticSlots(ModHelper mods, ICloner cloner, DatabaseService database) : IOnLoad
#endif
{
    private static readonly HashSet<string> Parents =
    [
        "5448e54d4bdc2dcc718b4568",
        "5448e5284bdc2dcb718b4567",
        "57bef4c42459772e8d35a53b",
        "5a341c4086f77401f2541505"
    ];

    private static readonly HashSet<string> VisualProperties =
    [
        "Prefab", "Name", "ShortName", "Description", "BackgroundColor"
    ];

#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if !AISHI_SPT41
        var templates = database.GetTemplates();
#endif
        var root = Path.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db", "CustomItems");
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), options);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var config = entry.Value;
                if (config.TryGetProperty("addtoModSlots", out var addToSlots) && addToSlots.ValueKind == JsonValueKind.True
                    && config.TryGetProperty("itemTplToClone", out var sourceTemplate)
                    && config.TryGetProperty("modSlot", out var modSlots) && modSlots.ValueKind == JsonValueKind.Array)
                {
                    var names = modSlots.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var originalId = sourceTemplate.GetString()!;
                    foreach (var target in templates.Items.Values)
                        foreach (var slot in target.Properties?.Slots ?? [])
                            if (slot.Name != null && names.Contains(slot.Name))
                                foreach (var filter in slot.Properties?.Filters ?? [])
                                    if (filter.Filter?.Contains(originalId) == true)
                                        filter.Filter.Add(entry.Name);
                }
                if (!config.TryGetProperty("parentId", out var parent) || !Parents.Contains(parent.GetString() ?? "")
                    || !config.TryGetProperty("overrideProperties", out var properties)
                    || properties.EnumerateObject().Any(property => !VisualProperties.Contains(property.Name))
                    || !config.TryGetProperty("itemTplToClone", out var mother))
                    continue;
                if (templates.Items.TryGetValue(entry.Name, out var variant)
                    && templates.Items.TryGetValue(mother.GetString()!, out var original)
                    && variant.Properties != null && original.Properties != null)
                    variant.Properties.Slots = cloner.Clone(original.Properties.Slots);
            }
        }

        return Task.CompletedTask;
    }
}
