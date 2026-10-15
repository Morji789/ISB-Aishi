using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Generators.Loot;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1201)]
public sealed class AishiAnomalyContainmentRegistration(InventoryConfig configuration) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        AishiAnomalyContainment.Configuration = configuration;
        return Task.CompletedTask;
    }
}

internal static class AishiAnomalyContainment
{
    internal const string Box = "c5c72a7a3bf1f02421a145bb";
    internal const string Container = "667a400d44455c10e40f761f";
    internal static InventoryConfig Configuration = null!;
    internal static bool IsSuppressed(Item item, IReadOnlyDictionary<string, Item> items)
    {
        var parent = item.ParentId;
        for (int i = 0; i < 64 && parent != null; i++)
        {
            if (!items.TryGetValue(parent, out var ancestor))
                return false;
            if (ancestor.Template == Container)
                return true;
            parent = ancestor.ParentId;
        }

        return parent != null;
    }
}

[HarmonyPatch(typeof(LootGenerator), "GetRandomLootContainerLoot")]
internal static class AishiAnomalyGuaranteedContainer
{
    private static void Postfix(RewardDetails __0, List<List<Item>> __result)
    {
        if (__result == null || AishiAnomalyContainment.Configuration?.RandomLootContainers?.TryGetValue(AishiAnomalyContainment.Box, out var reward) != true || !ReferenceEquals(__0, reward))
            return;
        __result.Add(new List<Item> { new Item { Id = new MongoId(), Template = AishiAnomalyContainment.Container } });
    }
}
