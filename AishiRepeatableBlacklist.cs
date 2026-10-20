using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
#if AISHI_SPT41
using SPTarkov.Server.Core.Generators.RepeatableQuests;
#else
using SPTarkov.Server.Core.Generators.RepeatableQuestGeneration;
#endif

namespace Aishi;

internal static class AishiRepeatableBlacklist
{
    internal static void Install(Harmony harmony)
    {
        foreach (var (name, patch) in new[]
        {
            ("GetRewardableItems", nameof(FilterPool)),
            (nameof(RepeatableQuestRewardGenerator.GenerateReward), nameof(FilterRewards))
        })
        {
            var target = AccessTools.Method(typeof(RepeatableQuestRewardGenerator), name)
                ?? throw new MissingMethodException(typeof(RepeatableQuestRewardGenerator).FullName, name);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(AishiRepeatableBlacklist), patch)
            {
                priority = Priority.Last,
                after = Harmony.GetPatchInfo(target)?.Owners.Where(owner => owner != AishiItemBlacklistPatch.HarmonyId).ToArray() ?? []
            });
        }
    }

    private static void FilterPool(MongoId traderId, ref List<TemplateItem> __result)
    {
        if (traderId.ToString() != AishiRepeatables.TraderId && __result != null)
            __result = __result.Where(item => !AishiItemBlacklistPatch.Policy.Contains(item.Id.ToString())).ToList();
    }

    private static void FilterRewards(MongoId traderId, Dictionary<string, List<Reward>>? __result)
    {
        if (traderId.ToString() == AishiRepeatables.TraderId || __result == null)
            return;
        foreach (var rewards in __result.Values)
            rewards.RemoveAll(reward => reward.Items?.Any(item => AishiItemBlacklistPatch.Policy.Contains(item.Template.ToString())) == true);
    }
}
