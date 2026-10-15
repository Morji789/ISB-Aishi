using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using HarmonyLib;
#if SPT41
using QuestClass = EFT.Quests.Quest;
using AbstractQuestControllerClass = EFT.Quests.QuestController;
#endif

namespace AishiKeys
{
    internal static class AishiUnlockChain
    {
        internal const string FastQuest = "c3dd45d0da2679c60db3c3cb";
        private static readonly HashSet<string> Alternatives = new HashSet<string>(StringComparer.Ordinal)
        {
            "690e2689cbd10f2ac5e766eb", "690bcb580b8f9c461c3eae53",
            "690d8ad1750d535b2e9bc135", "690bb986148ac0cfb2669d32"
        };

        internal static bool Completed(AbstractQuestControllerClass controller)
        {
            return controller.Profile.QuestsData.Any(quest => quest.Id == FastQuest && quest.Status == EQuestStatus.Success)
                || controller.Quests.Any(quest => quest.Id == FastQuest && quest.QuestStatus == EQuestStatus.Success);
        }

        internal static void Fail(QuestClass quest)
        {
            if (quest?.Template == null || !Alternatives.Contains(quest.Id) || quest.QuestStatus == EQuestStatus.Success)
                return;
            quest.Template.Restartable = false;
            quest.Template.AppearStatus = EQuestStatus.Fail;
            quest.SetStatus(EQuestStatus.Fail, false, false);
        }
    }

    [HarmonyPatch(typeof(AbstractQuestControllerClass), nameof(AbstractQuestControllerClass.ManageConditional))]
    internal static class AishiUnlockChainInitialize
    {
        [HarmonyPostfix]
        private static void AfterManage(AbstractQuestControllerClass __instance, QuestClass conditional)
        {
            if (AishiUnlockChain.Completed(__instance))
                AishiUnlockChain.Fail(conditional);
        }
    }

    [HarmonyPatch(typeof(AbstractQuestControllerClass), nameof(AbstractQuestControllerClass.OnConditionalStatusChangedEvent))]
    internal static class AishiUnlockChainComplete
    {
        [HarmonyPostfix]
        private static void AfterChange(AbstractQuestControllerClass __instance, QuestClass conditional)
        {
            if (conditional.Id != AishiUnlockChain.FastQuest || conditional.QuestStatus != EQuestStatus.Success)
                return;
            foreach (var quest in __instance.Quests)
                AishiUnlockChain.Fail(quest);
        }
    }
}
