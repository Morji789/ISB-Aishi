using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI.Ragfair;
using HarmonyLib;
#if SPT41
using Assortment = EFT.Trading.Assortment;

#else
using Assortment = TraderAssortmentControllerClass;
#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(Assortment), "get_SelectedItemBuyRestrictionMax")]
    internal static class AishiDvxPurchaseLimit
    {
        internal static bool IsEventOffer(Item item) => item?.Id == "4a242c6a3685ba67964570ac" && item.StringTemplateId == "6a426637ddc63098d100ae67";
        private static void Postfix(Assortment __instance, ref int __result)
        {
            if (IsEventOffer(__instance.SelectedItem))
                __result = 120;
        }
    }

    [HarmonyPatch(typeof(Offer), "get_BuyRestrictionMax")]
    internal static class AishiDvxFleaLimit
    {
        private static void Postfix(Offer __instance, ref int __result)
        {
            if (AishiDvxPurchaseLimit.IsEventOffer(__instance.Item))
                __result = 120;
        }
    }
}
