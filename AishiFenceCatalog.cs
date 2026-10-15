using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using Path = System.IO.Path;
#if AISHI_SPT41
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Services.Commerce;
#else
using SPTarkov.Server.Core.Services;
#endif

namespace Aishi;

[Injectable(TypePriority = int.MaxValue - 21)]
#if AISHI_SPT41
public sealed class AishiFenceCatalog(TraderConfig configs) : IOnLoad
#else
public sealed class AishiFenceCatalog(ConfigServer configs) : IOnLoad
#endif
{
    private static readonly HashSet<MongoId> BlueForce = [];
#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "db", "CustomItems"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var item in document.RootElement.EnumerateObject())
                if (item.Value.TryGetProperty("locales", out var locales) && locales.TryGetProperty("en", out var english) && english.TryGetProperty("name", out var name) && name.GetString()?.Contains("Blue Force Gear", StringComparison.OrdinalIgnoreCase) == true)
                    BlueForce.Add(item.Name);
        }
#if AISHI_SPT41
        var fence = configs.Fence;
#else
        var fence = configs.GetConfig<TraderConfig>().Fence;
#endif
        foreach (var id in BlueForce)
            if (!fence.Blacklist.Contains(id))
                fence.Blacklist.Add(id);
        new Harmony("com.samc137.aishi.fence-catalog").Patch(AccessTools.Method(typeof(FenceService), "AddItemAssorts"), prefix: new HarmonyMethod(typeof(AishiFenceCatalog), nameof(FilterPool)));
        return Task.CompletedTask;
    }

    private static void FilterPool(TraderAssort __2)
    {
        var removed = __2.Items.Where(item => item.ParentId == "hideout" && BlueForce.Contains(item.Template)).Select(item => item.Id.ToString()).ToHashSet();
        int count;
        do
        {
            count = removed.Count;
            foreach (var item in __2.Items.Where(item => item.ParentId != null && removed.Contains(item.ParentId)))
                removed.Add(item.Id.ToString());
        } while (count != removed.Count);
        __2.Items.RemoveAll(item => removed.Contains(item.Id.ToString()));
        foreach (var id in removed)
        {
            __2.BarterScheme?.Remove(id);
            __2.LoyalLevelItems?.Remove(id);
        }
    }
}
