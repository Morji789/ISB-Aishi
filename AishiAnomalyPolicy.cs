using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1200)]
public sealed class AishiAnomalyPolicy(TemplateTable templates, TradersTable traders, ModHelper modHelper, SaveServer saves, MailSendService mail, AishiAnomalyConfig config) : IOnLoad
{
    internal static readonly HashSet<MongoId> BeltIds = new()
    {
        "6761b213607f9a6f79017aef",
            "6761b213607f9a6f79017af1",
            "6761b213607f9a6f79017af2",
            "6761b213607f9a6f79017af3",
            "6761b213607f9a6f79017af4",
            "6761b213607f9a6f79017af5",
            "6761b213607f9a6f79017af6",
            "6761b213607f9a6f79017af7",
            "6761b213607f9a6f79017af9",
            "6761b213607f9a6f79017afa",
            "6761b213607f9a6f79017afb",
            "6761b213607f9a6f79017afc",
            "6761b213607f9a6f79017afd",
            "6761b213607f9a6f79017afe",
            "6761b213607f9a6f79017aff",
            "6807e54dc5a45c5443154a29",
            "68084891d93e159932d7e326",
            "680e8d81e16a7caa3dd10730",
            "68170b2cb595bf820b60571a",
            "69352d5c409abe93d29321e4",
            "681baec29eb1732a417bd7dd",
            "681bb00c7fecf6d490b2fb3c",
            "681bcd300480896883303f71",
            "69364a368a4b47eeaf9ba4a3"
    };
    internal static readonly HashSet<MongoId> Ids = new();
    internal static SaveServer Saves = null!;
    internal static MailSendService Mail = null!;
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Saves = saves;
        Mail = mail;
        var folder = System.IO.Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db", "CustomItems");
        Ids.Clear();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Name.Length == 24 && entry.Value.TryGetProperty("locales", out var locales) && locales.TryGetProperty("en", out var locale) && locale.TryGetProperty("name", out var name) && name.GetString()?.StartsWith("KTE ", StringComparison.Ordinal) == true)
                    Ids.Add(entry.Name);
        }

        foreach (var trader in traders.Values)
        {
            var settings = trader.Base;
            if (settings == null)
                continue;
            if (settings.Id == DollarServicePolicy.AishiId)
            {
                settings.ItemsBuy ??= new ItemBuyData
                {
                    Category = new HashSet<MongoId>(),
                    IdList = new HashSet<MongoId>()
                };
                (settings.ItemsBuy.IdList ??= new HashSet<MongoId>()).UnionWith(Ids);
                settings.ItemsBuyProhibited?.IdList?.ExceptWith(Ids);
            }
            else
            {
                settings.ItemsBuy?.IdList?.ExceptWith(Ids);
                settings.ItemsBuyProhibited ??= new ItemBuyData
                {
                    Category = new HashSet<MongoId>(),
                    IdList = new HashSet<MongoId>()
                };
                (settings.ItemsBuyProhibited.IdList ??= new HashSet<MongoId>()).UnionWith(Ids);
            }
        }

        var restricted = new HashSet<MongoId>(Ids)
        {
            AishiAnomalyContainment.Container
        };
        var items = templates.Items;
        foreach (var item in items.Values)
        {
            var parent = item;
            bool secure = false;
            for (int i = 0; i < 32 && parent != null; i++)
            {
                if (parent.Id == "5448bf274bdc2dfc2f8b456a")
                {
                    secure = true;
                    break;
                }

                if (!items.TryGetValue(parent.Parent, out parent))
                    break;
            }

            if ((secure || BeltIds.Contains(item.Id)) && item.Properties?.Grids != null)
                foreach (var grid in item.Properties.Grids)
                {
                    var filters = grid.Properties.Filters?.ToList() ?? new List<GridFilter>();
                    if (filters.Count == 0)
                        filters.Add(new GridFilter { Filter = new HashSet<MongoId> { "54009119af1c881c07000029" } });
                    foreach (var filter in filters)
                        {
                        if (config.Debug.RemoveKTErestrictions)
                        {
                            filter.ExcludedFilter?.ExceptWith(restricted);
                            (filter.Filter ??= new HashSet<MongoId>()).UnionWith(restricted);
                        }
                        else
                            (filter.ExcludedFilter ??= new HashSet<MongoId>()).UnionWith(restricted);
                    }
                    grid.Properties.Filters = filters;
                }

            if (item.Properties?.Slots == null)
                continue;
            foreach (var slot in item.Properties.Slots.Where(s => s.Name?.StartsWith("SpecialSlot", StringComparison.OrdinalIgnoreCase) == true))
                foreach (var filter in slot.Properties.Filters ?? [])
                {
                    if (config.Debug.RemoveKTErestrictions)
                        (filter.Filter ??= new HashSet<MongoId>()).UnionWith(restricted);
                    else
                        filter.Filter?.ExceptWith(restricted);
                    filter.ExtensionData ??= new Dictionary<string, object>();
                    var excluded = new HashSet<string>(StringComparer.Ordinal);
                    if (filter.ExtensionData.TryGetValue("ExcludedFilter", out var previous))
                    {
                        if (previous is IEnumerable<MongoId> mongoIds)
                            excluded.UnionWith(mongoIds.Select(id => id.ToString()));
                        else if (previous is IEnumerable<string> strings)
                            excluded.UnionWith(strings);
                        else if (previous is JsonElement element && element.ValueKind == JsonValueKind.Array)
                            excluded.UnionWith(element.EnumerateArray().Select(id => id.GetString()!).Where(id => id != null));
                    }

                    if (config.Debug.RemoveKTErestrictions)
                        excluded.ExceptWith(restricted.Select(id => id.ToString()));
                    else
                        excluded.UnionWith(restricted.Select(id => id.ToString()));
                    filter.ExtensionData["ExcludedFilter"] = excluded;
                }
        }

        return Task.CompletedTask;
    }
}

internal static class AnomalyDeathProtection
{
    internal const string Disk = "73cc246044b1d66dd70aedb7";
    private const string Receipt = "aishiAnomalyRecoveredRaid";
    private sealed class Protection
    {
        internal HashSet<string> Items = new(StringComparer.Ordinal);
    }

    private static readonly ConditionalWeakTable<PmcData, Protection> Protected = new();
    internal static HashSet<string> EquipmentItems(PmcData profile)
    {
        var items = profile.Inventory?.Items;
        var root = profile.Inventory?.Equipment?.ToString();
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (items == null || root == null)
            return found;
        var children = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!);
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
            foreach (var child in children[queue.Dequeue()])
                if (found.Add(child.Id.ToString()))
                    queue.Enqueue(child.Id.ToString());
        return found;
    }

    internal static void NormalizeHealth(PmcData before, PmcData after)
    {
        var inventory = after.Inventory?.Items;
        if (inventory != null)
        {
            var depleted = inventory.Where(item => item.Template == "ae711ed0c5878f74a4264e27" && item.Upd?.Resource?.Value <= 0).Select(item => item.Id).ToHashSet();
            if (depleted.Count > 0)
            {
                inventory.RemoveAll(item => depleted.Contains(item.Id));
                after.InsuredItems?.RemoveAll(item => item.ItemId is { } id && depleted.Contains(id));
            }
        }

        if (before.Health?.BodyParts == null || after.Health?.BodyParts == null)
            return;
        foreach (var pair in after.Health.BodyParts)
            if (before.Health.BodyParts.TryGetValue(pair.Key, out var baseline) && baseline.Health?.Maximum is double maximum && pair.Value.Health != null)
            {
                pair.Value.Health.Maximum = maximum;
                pair.Value.Health.Current = Math.Min(pair.Value.Health.Current ?? 0, maximum);
            }
    }

    internal static bool AlreadyRecovered(SptProfile profile, string raid) => profile.ExtensionData?.TryGetValue(Receipt, out var value) == true && value?.ToString() == raid;
    internal static bool Begin(MongoId session, PmcData target, EndLocalRaidRequestData request, bool scav)
    {
        if (request.Results?.Profile?.Inventory?.Items == null || string.IsNullOrEmpty(request.ServerId))
            return false;
        NormalizeHealth(target, request.Results.Profile);
        if (request.Results.Result != ExitStatus.KILLED)
            return false;
        var profile = AishiAnomalyPolicy.Saves.GetProfile(session);
        if (AlreadyRecovered(profile, request.ServerId))
            return false;
        var post = request.Results.Profile;
        var equipped = EquipmentItems(post);
        var byId = post.Inventory.Items.GroupBy(i => i.Id.ToString()).ToDictionary(g => g.Key, g => g.First());
        var disk = post.Inventory.Items.FirstOrDefault(i => i.Template == Disk && equipped.Contains(i.Id.ToString()) && !AishiAnomalyContainment.IsSuppressed(i, byId));
        if (disk == null)
            return false;
        if (disk.Upd?.StackObjectsCount > 1)
            disk.Upd.StackObjectsCount--;
        else
        {
            post.Inventory.Items.Remove(disk);
            equipped.Remove(disk.Id.ToString());
        }

        Protected.Remove(target);
        Protected.Add(target, new Protection { Items = equipped });
        request.LostInsuredItems = request.LostInsuredItems?.Where(i => i.Id != disk.Id && !equipped.Contains(i.Id.ToString())).ToList();
        if (!post.Inventory.Items.Contains(disk))
            post.InsuredItems?.RemoveAll(i => i.ItemId == disk.Id);
        profile.ExtensionData ??= new Dictionary<string, object>();
        profile.ExtensionData[Receipt] = request.ServerId;
        if (scav)
        {
            var preserved = post.Inventory.Items.Where(i => equipped.Contains(i.Id.ToString())).ToList();
            foreach (var item in preserved)
            {
                if (item.Upd != null)
                    item.Upd.SpawnedInSession = false;
                if (item.ParentId == post.Inventory.Equipment?.ToString())
                {
                    item.ParentId = null;
                    item.SlotId = null;
                    item.Location = null;
                }
            }

            AishiAnomalyPolicy.Mail.SendLocalisedNpcMessageToPlayer(session, DollarServicePolicy.AishiId, MessageType.MessageWithItems, "aishiAnomalyRecoveredItems", preserved, 2592000);
        }

        return true;
    }

    internal static IEnumerable<Item> FilterLosses(PmcData profile, IEnumerable<Item> losses) => Protected.TryGetValue(profile, out var protection) ? losses.Where(i => !protection.Items.Contains(i.Id.ToString())) : losses;
    internal static void Finish(PmcData profile) => Protected.Remove(profile);
    internal static void RollbackReceipt(MongoId session, string raid)
    {
        var profile = AishiAnomalyPolicy.Saves.GetProfile(session);
        if (AlreadyRecovered(profile, raid))
            profile.ExtensionData!.Remove(Receipt);
    }
}

[HarmonyPatch(typeof(LocationLifecycleService), "EndLocalRaidAsync")]
internal static class AnomalyRecoveredRaidReplay
{
    private static bool Prefix(MongoId __0, EndLocalRaidRequestData __1, ref Task __result)
    {
        if (!AnomalyDeathProtection.AlreadyRecovered(AishiAnomalyPolicy.Saves.GetProfile(__0), __1.ServerId))
            return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static void Postfix(MongoId __0, EndLocalRaidRequestData __1, ref Task __result) => __result = Finish(__result, __0, __1.ServerId);
    private static async Task Finish(Task original, MongoId session, string raid)
    {
        try
        {
            await original;
        }
        catch
        {
            AnomalyDeathProtection.RollbackReceipt(session, raid);
            throw;
        }
    }

    private static void Finalizer(MongoId __0, EndLocalRaidRequestData __1, Exception? __exception)
    {
        if (__exception != null)
            AnomalyDeathProtection.RollbackReceipt(__0, __1.ServerId);
    }
}

[HarmonyPatch(typeof(LocationLifecycleService), "HandlePostRaidPmc")]
internal static class AnomalyPmcRecovery
{
    private static void Prefix(MongoId __0, SptProfile __1, EndLocalRaidRequestData __6) => AnomalyDeathProtection.Begin(__0, __1.CharacterData.PmcData, __6, false);
    private static void Finalizer(SptProfile __1) => AnomalyDeathProtection.Finish(__1.CharacterData.PmcData);
}

[HarmonyPatch(typeof(LocationLifecycleService), "HandlePostRaidPlayerScavAsync")]
internal static class AnomalyScavRecovery
{
    private static void Prefix(MongoId __0, PmcData __2, EndLocalRaidRequestData __6) => AnomalyDeathProtection.Begin(__0, __2, __6, true);
    private static void Finalizer(PmcData __2) => AnomalyDeathProtection.Finish(__2);
}

[HarmonyPatch(typeof(InRaidHelper), "GetInventoryItemsLostOnDeath")]
internal static class AnomalyInventoryRecovery
{
    private static void Postfix(PmcData __0, ref IEnumerable<Item> __result) => __result = AnomalyDeathProtection.FilterLosses(__0, __result);
}
