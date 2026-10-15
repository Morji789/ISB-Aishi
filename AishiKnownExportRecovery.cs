using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using WTTServerCommonLib.Models;
using WTTServerCommonLib.Services;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public sealed class AishiKnownExportRecovery(TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        KnownAishiExportPolicy.Bind(templates.Items, templates.Handbook.Items);
        new Harmony("com.samc137.aishi.known-export-recovery").Patch(AccessTools.Method(typeof(WTTCustomItemServiceExtended), "CreateItemFromClone"), prefix: new HarmonyMethod(typeof(KnownAishiExportPolicy), nameof(KnownAishiExportPolicy.BeforeClone)), finalizer: new HarmonyMethod(typeof(KnownAishiExportPolicy), nameof(KnownAishiExportPolicy.RestoreOnFailure)));
        return Task.CompletedTask;
    }
}

public static class KnownAishiExportPolicy
{
    public const string Dm11 = "6a30ec62eb1babffc84227d8";
    public const string Dm11Pack = "6a30ec66d725140ed59cdd1c";
    private static IDictionary<MongoId, TemplateItem> items = null!;
    private static IList<HandbookItem> handbook = null!;
    private static readonly HashSet<string> Reconciled = new(StringComparer.Ordinal);
    public sealed class RemovedExport
    {
        internal string Id = "";
        internal TemplateItem Item = null!;
        internal List<HandbookItem> Handbook = [];
    }

    public static void Bind(IDictionary<MongoId, TemplateItem> templates, IList<HandbookItem> prices)
    {
        items = templates;
        handbook = prices;
        Reconciled.Clear();
    }

    public static bool Matches(string id, TemplateItem existing, CustomItemConfig config)
    {
        if (id is not (Dm11 or Dm11Pack) || existing.Id.ToString() != id || existing.Parent.ToString() != config.ParentId || existing.Properties == null || existing.Name?.StartsWith("[Aishi]", StringComparison.Ordinal) != true || !existing.Name.Contains("4.6x30mm DM11", StringComparison.Ordinal))
            return false;
        if (id == Dm11)
        {
            var path = existing.Properties.Prefab?.Path;
            return existing.Parent.ToString() == "5485a8684bdc2da71d8b4567" && existing.Properties.Caliber?.Contains("46x30", StringComparison.OrdinalIgnoreCase) == true && existing.Properties.Damage == 46 && existing.Properties.PenetrationPower == 49 && (path == "ammo/dm11_46x30/patron_46x30_dm11_finishes.bundle" || !string.IsNullOrWhiteSpace(path) && path == config.OverrideProperties?.Prefab?.Path);
        }

        if (existing.Parent.ToString() != "543be5cb4bdc2deb348b4568" || existing.Properties.Prefab?.Path != "assets/content/items/ammo/boxes/item_ammo_box_4_6x30mm.bundle")
            return false;
        return existing.Properties.StackSlots?.Any(slot => slot.MaxCount == 40 && slot.Properties?.Filters?.Any(filter => filter.Filter?.Any(value => value.ToString() == Dm11) == true) == true) == true;
    }

    public static void BeforeClone(Assembly __0, string __1, CustomItemConfig __2, out RemovedExport? __state)
    {
        __state = null;
        if (__0 != typeof(AishiKnownExportRecovery).Assembly || Reconciled.Contains(__1) || !items.TryGetValue(__1, out var existing) || !Matches(__1, existing, __2))
            return;
        var removed = new RemovedExport
        {
            Id = __1,
            Item = existing,
            Handbook = handbook.Where(value => value.Id.ToString() == __1).ToList()
        };
        foreach (var entry in removed.Handbook)
            handbook.Remove(entry);
        items.Remove(__1);
        Reconciled.Add(__1);
        __state = removed;
    }

    public static Exception? RestoreOnFailure(Exception? __exception, RemovedExport? __state)
    {
        if (__exception == null || __state == null)
            return __exception;
        items[__state.Id] = __state.Item;
        foreach (var added in handbook.Where(value => value.Id.ToString() == __state.Id).ToArray())
            handbook.Remove(added);
        foreach (var entry in __state.Handbook)
            handbook.Add(entry);
        Reconciled.Remove(__state.Id);
        return __exception;
    }
}
