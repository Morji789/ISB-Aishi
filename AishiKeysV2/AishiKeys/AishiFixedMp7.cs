using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
#if SPT41
using Factory = EFT.ItemFactory;
#else
using Factory = ItemFactoryClass;

#endif
namespace AishiKeys
{
    internal static class AishiFixedMp7
    {
        internal const string Template = "6a4d24532666085a03d7b1e3";
        internal static bool Matches(Item item) => item != null && item.TemplateId == Template;
    }

    [HarmonyPatch]
    internal static class AishiFixedMp7Create
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Factory), "CreateItem");
        private static void Postfix(Item __result)
        {
            if (!AishiFixedMp7.Matches(__result))
                return;
            var folding = __result.GetItemComponent<FoldableComponent>();
            if (folding != null)
                folding.Folded = true;
        }
    }

    [HarmonyPatch(typeof(FoldableComponent), "get_CanBeFolded")]
    internal static class AishiFixedMp7Interaction
    {
        private static void Postfix(FoldableComponent __instance, ref bool __result)
        {
            if (AishiFixedMp7.Matches(__instance.Item))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(FoldableComponent), "SetFolded")]
    internal static class AishiFixedMp7State
    {
        private static void Prefix(FoldableComponent __instance, ref bool __0)
        {
            if (AishiFixedMp7.Matches(__instance.Item))
                __0 = true;
        }
    }
}
