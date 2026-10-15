using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
#if AISHI_SPT41
using SPTarkov.Server.Core.Models.Spt.Tables;
#endif

namespace Aishi;
[Injectable(TypePriority = int.MaxValue - 150)]
#if AISHI_SPT41
public sealed class AishiHammerheadEquipment(TemplateTable templates) : IOnLoad
#else
public sealed class AishiHammerheadEquipment(DatabaseService database) : IOnLoad
#endif
{
    private static readonly HashSet<MongoId> AllowedHeadsets = ["a5f079ec80165ad68b7ac167", "69413241b1ce1e5fbb09ed0a", "68bf405779c8186398099017"];
    internal const string Helmet = "7f3d1490eb7e5442947f28f5";
#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if AISHI_SPT41
        var items = templates.Items;
#else
        var items = database.GetItems();
#endif
        if (!items.TryGetValue(Helmet, out var hammerhead)) return Task.CompletedTask;
        var conflicts = hammerhead.Properties.ConflictingItems?.ToHashSet() ?? new HashSet<MongoId>();
        foreach (var item in items.Values)
        {
            if (item.Id == Helmet) continue;
            if (AllowedHeadsets.Contains(item.Id))
                item.Properties.ConflictingItems?.Remove(Helmet);
            var ancestor = item;
            for (int depth = 0; depth < 32; depth++)
            {
                if (ancestor.Parent == "5a341c4086f77401f2541505" || ancestor.Parent == "5645bcb74bdc2ded0b8b4578")
                {
                    conflicts.Add(item.Id);
                    break;
                }
                if (!items.TryGetValue(ancestor.Parent, out ancestor)) break;
            }
            foreach (var slot in item.Properties?.Slots ?? [])
            {
                if (slot.Name != "Headwear") continue;
                foreach (var filter in slot.Properties.Filters ?? [])
                {
                    filter.Filter?.Remove(Helmet);
                    filter.ExtensionData ??= new Dictionary<string, object>();
                    var excluded = new HashSet<string>(StringComparer.Ordinal) { Helmet };
                    if (filter.ExtensionData.TryGetValue("ExcludedFilter", out var old))
                    {
                        if (old is IEnumerable<MongoId> ids) excluded.UnionWith(ids.Select(id => id.ToString()));
                        else if (old is IEnumerable<string> names) excluded.UnionWith(names);
                        else if (old is JsonElement array && array.ValueKind == JsonValueKind.Array)
                            excluded.UnionWith(array.EnumerateArray().Select(id => id.GetString()!).Where(id => id != null));
                    }
                    filter.ExtensionData["ExcludedFilter"] = excluded;
                }
            }
        }
        conflicts.ExceptWith(AllowedHeadsets);
        hammerhead.Properties.ConflictingItems = conflicts;
        return Task.CompletedTask;
    }
}
