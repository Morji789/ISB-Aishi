using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Path = System.IO.Path;
using SPTarkov.Server.Core.Helpers;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 725)]
public sealed class AishiItemQualitySafety(ModHelper modHelper) : IOnLoad
{
    internal static readonly HashSet<string> ItemIds = new(StringComparer.Ordinal);
    public Task OnLoad()
    {
        var root = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Name.Length == 24 && entry.Name.All(Uri.IsHexDigit) && entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("itemTplToClone", out _))
                    ItemIds.Add(entry.Name);
        }

        new Harmony("com.samc137.aishi.item-quality-safety").Patch(AccessTools.Method(typeof(ItemHelper), nameof(ItemHelper.GetItemQualityModifier)), postfix: new HarmonyMethod(typeof(AishiItemQualitySafety), nameof(AfterQuality)));
        return Task.CompletedTask;
    }

    private static void AfterQuality(Item __0, ref double __result)
    {
        if (!double.IsFinite(__result) && ItemIds.Contains(__0.Template.ToString()))
            __result = 1d;
    }
}
