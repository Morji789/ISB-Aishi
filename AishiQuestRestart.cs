using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Helpers.Quest;

namespace Aishi;
[HarmonyPatch(typeof(QuestHelper), nameof(QuestHelper.ResetQuestState))]
internal static class AishiQuestRestart
{
    [HarmonyPostfix]
    private static void AfterReset(QuestHelper __instance, PmcData pmcData, QuestStatusEnum newQuestState, MongoId questId)
    {
        if (newQuestState != QuestStatusEnum.Started)
            return;
        var quest = __instance.GetQuestFromDb(questId, pmcData);
        if (quest == null || quest.TraderId != "690766de550bc322a810ea1e" || quest.Restartable != true)
            return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (quest.Conditions != null)
            foreach (var condition in (quest.Conditions.AvailableForFinish ?? []).Concat(quest.Conditions.AvailableForStart ?? []).Concat(quest.Conditions.Fail ?? []))
            {
                ids.Add(condition.Id.ToString());
                if (!string.IsNullOrEmpty(condition.Counter?.Id))
                    ids.Add(condition.Counter.Id);
            }

        if (pmcData.TaskConditionCounters != null)
            foreach (var pair in pmcData.TaskConditionCounters)
                if (pair.Value.SourceId == questId || ids.Contains(pair.Key.ToString()))
                    pair.Value.Value = 0;
        var stats = pmcData.Stats?.Eft;
        if (stats?.DroppedItems != null)
            stats.DroppedItems = stats.DroppedItems.Where(x => x.QuestId != questId).ToList();
        if (stats?.FoundInRaidItems != null)
            stats.FoundInRaidItems = stats.FoundInRaidItems.Where(x => x.QuestId != questId).ToList();
    }
}
