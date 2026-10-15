using System;
using System.Linq;
using EFT.Quests;
using HarmonyLib;

namespace AishiKeys
{
    [HarmonyPatch(typeof(QuestControllerAbstractClass<QuestClass>), nameof(QuestControllerAbstractClass<QuestClass>.ClearConditions))]
    internal static class AishiQuestRestart
    {
        [HarmonyPostfix]
        private static void AfterClear(QuestControllerAbstractClass<QuestClass> __instance, QuestClass conditional)
        {
            if (conditional?.Template == null || !conditional.Template.Restartable || !string.Equals(conditional.Template.TraderId, AishiDollarUi.TraderId, StringComparison.Ordinal))
                return;
            var ids = new System.Collections.Generic.HashSet<string>(conditional.Conditions.SelectMany(group => group.Value).Select(condition => condition.id.ToString()), StringComparer.Ordinal);
            foreach (var pair in __instance.Profile_0.TaskConditionCounters)
                if (pair.Value.SourceId == conditional.Id || ids.Contains(pair.Key.ToString()))
                    pair.Value.Value = 0;
            conditional.CompletedConditions.Clear();
        }
    }
}
