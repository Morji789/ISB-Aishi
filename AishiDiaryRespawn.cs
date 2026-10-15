using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;

namespace Aishi;
internal static class DiaryPickupRecovery
{
    internal const string Diary = "690e15d7799d853668054517";
    private static readonly (string Quest, string Find, string Handover)[] Bindings = [("690d8ad1750d535b2e9bc135", "690d8c27dc1e0cb5c27bf5be", "690d8c38c30e738ddabfdb8b"), ("690e2689cbd10f2ac5e766eb", "6937a89eda352f46b8a30100", "690e26a05d880ff9624676c3"), ];
    internal static int Repair(PmcData? pmc, bool diaryLostInRaid = false)
    {
        if (pmc?.Inventory?.Items == null || pmc.Quests == null)
            return 0;
        var raidRoot = pmc.Inventory.QuestRaidItems?.ToString();
        if (pmc.Inventory.Items.Any(item => item.Template == Diary && (!diaryLostInRaid || raidRoot == null || item.ParentId != raidRoot)))
            return 0;
        int repaired = 0;
        foreach (var binding in Bindings)
        {
            var quest = pmc.Quests.FirstOrDefault(quest => quest.QId == binding.Quest);
            if (quest?.Status is not (QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish))
                continue;
            var completed = quest.CompletedConditions;
            if (completed == null || !completed.Contains(binding.Find))
                continue;
            if (completed.Contains(binding.Handover))
                continue;
            if (pmc.TaskConditionCounters?.TryGetValue(binding.Handover, out var delivered) == true && delivered.Value.GetValueOrDefault() >= 1)
                continue;
            completed.RemoveAll(condition => condition == binding.Find);
            repaired++;
        }

        return repaired;
    }

    internal static IEnumerable<Item> HandleLostDiary(PmcData pmc, IEnumerable<Item> lostItems)
    {
        var items = lostItems.ToArray();
        if (!items.Any(item => item.Template == Diary))
            return items;
        Repair(pmc, diaryLostInRaid: true);
        return items.Where(item => item.Template != Diary).ToArray();
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1101)]
public sealed class AishiDiaryRespawn(SaveServer saves) : IOnLoad
{
    private static AishiDiaryRespawn? _instance;
    private readonly SaveServer _saves = saves;
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _instance = this;
        var harmony = new Harmony("com.samc137.aishi.diary-respawn");
        harmony.Patch(AccessTools.Method(typeof(LocationLifecycleService), "CheckForAndFixPickupQuestsAfterDeath"), prefix: new HarmonyMethod(typeof(AishiDiaryRespawn), nameof(BeforePickupQuestReset)));
        harmony.Patch(AccessTools.Method(typeof(GameController), nameof(GameController.GameStart)), prefix: new HarmonyMethod(typeof(AishiDiaryRespawn), nameof(BeforeGameStart)));
        return Task.CompletedTask;
    }

    private static PmcData? GetPmc(MongoId sessionId)
    {
        var self = _instance;
        if (self == null || sessionId.IsEmpty || !self._saves.ProfileExists(sessionId))
            return null;
        var full = self._saves.GetProfile(sessionId);
        if (full.ProfileInfo?.IsWiped == true || full.ProfileInfo?.InvalidOrUnloadableProfile == true)
            return null;
        return full.CharacterData?.PmcData;
    }

    private static void BeforeGameStart(MongoId __1)
    {
        var pmc = GetPmc(__1);
        if (pmc == null)
            return;
        lock (pmc)
            DiaryPickupRecovery.Repair(pmc);
    }

    private static void BeforePickupQuestReset(MongoId __0, ref IEnumerable<Item> __1)
    {
        var pmc = GetPmc(__0);
        if (pmc == null || __1 == null)
            return;
        lock (pmc)
            __1 = DiaryPickupRecovery.HandleLostDiary(pmc, __1);
    }
}
