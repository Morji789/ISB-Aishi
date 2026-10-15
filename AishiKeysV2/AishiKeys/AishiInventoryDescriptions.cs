using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace AishiKeys
{
    [HarmonyPatch(typeof(Item), "get_Description")]
    internal static class AishiInventoryDescriptions
    {
        private static readonly HashSet<string> Anomalies = new HashSet<string>(StringComparer.Ordinal)
        {
            "b27508815b9307ccd1bffa36",
            "1800c994e11cfa35b423e8ed",
            "6183f2a51cb013b5ab62a796",
            "fac4f1a658ad562cddde72c6",
            "72eb2bb2416ee48ee80d63d4",
            "a38cbf3abbfdcbedaa1b9a0e",
            "b543125009b989d7287b58ab",
            "9644da05612ad219e7fc4387",
            "cd19a7a32ec3820a6ea48800",
            "1254eb68d298789e43b45ed3",
            "36be503998aadd53701c57e3",
            "f131c988930a86c7e5610c57",
            "45e6cfe53aabe0b5e846cad5",
            "9df4b9812799170d2f7295f7",
            "57d2c307a64e179e8995e061",
            "90c688feeba7c89aa1a3c8a7",
            "4a2e4aaef50cda01112c41b2",
            "73cc246044b1d66dd70aedb7",
            "06f0d5bdc44c4ddcd0759d31",
            "a8d639d499ac25709f630d2c",
            "922129d70c57a265f61ee8b1",
            "d180bb453e8bb2027c5fa540",
            "51574a824fe10af4518191af",
            "0056a97d873bba252113c74b",
            AishiAnomalyDuck.Template,
            "d94f4a94edf1050b40c93cdb",
            "eee72a686853f282c7cd01da",
            "25dd7397974f7d7940ad9742",
            "68c80d9fb3ef768bbe58a072",
            "d88925f5ddd4cd8795d5c9bd",
            "a2c16b836b441dc953652bd6",
            "571e1ce5aa8a32c10b70d206",
            "3ddc22a4b7e5ba67ee38edf9",
            "b7e0639e756abd3bbb872c2c",
            "337052e7f499ba2f52dfa089",
            "397495f6cda093c895814c0f",
            "e57cfb4c1b58a40f9032db70"
        };
        internal static bool IsAnomaly(string templateId) => Anomalies.Contains(templateId);
        private static void Postfix(Item __instance, ref string __result)
        {
            if (!Anomalies.Contains(__instance.TemplateId))
                return;
            __result = __instance.TemplateId + (AnomalyRevealClient.IsRevealed(__instance.TemplateId) ? " RevealedDescription" : " Description");
        }
    }

    [HarmonyPatch(typeof(TradingPlayerPanel), "UpdateStats")]
    internal static class AishiTradingSpendDisplay
    {
        private static void Postfix(Profile.TraderInfo __0, TMPro.TMP_Text ____currentMoney, TMPro.TMP_Text ____nextMoney)
        {
            bool visible = __0.Id != AishiDollarUi.TraderId;
            ____currentMoney.gameObject.SetActive(visible);
            ____nextMoney.gameObject.SetActive(visible);
        }
    }

    [HarmonyPatch(typeof(TraderTooltip), "Show")]
    internal static class AishiTradingSpendTooltip
    {
        private static void Postfix(Profile.TraderInfo __0, TMPro.TMP_Text ____moneySpent, TMPro.TMP_Text ____moneySpentRequired, GameObject ____moneySpentMet)
        {
            bool visible = __0.Id != AishiDollarUi.TraderId;
            ____moneySpent.gameObject.SetActive(visible);
            ____moneySpentRequired.gameObject.SetActive(visible);
            if (!visible)
                ____moneySpentMet.SetActive(false);
        }
    }
}
