using EFT.InventoryLogic;
using HarmonyLib;
using Trader = EFT.Trading.Trader;
using ItemPrice = EFT.Trading.Trader.ItemPrice;

namespace AishiKeys
{
    [HarmonyPatch(typeof(Trader), "GetUserItemPrice")]
    internal static class AishiBuyback
    {
        private static void Postfix(Trader __instance, Item __0, ref ItemPrice? __result)
        {
            if (__instance.Info.Id != AishiDollarUi.TraderId || !AishiCustomItemCatalog.Ids.Contains(__0.TemplateId) || !__instance.Info.CanBuyItem(__0))
                return;
            if (!__result.HasValue || __result.Value.Amount < 1)
                __result = new ItemPrice(CurrencyUtil.GetCurrencyId(ECurrencyType.USD), 1);
        }
    }
}
