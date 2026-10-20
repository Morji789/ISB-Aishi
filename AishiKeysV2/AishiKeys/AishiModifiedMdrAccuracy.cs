using AishiShared;
using EFT.InventoryLogic;
using HarmonyLib;

namespace AishiKeys
{
    [HarmonyPatch(typeof(Weapon), nameof(Weapon.GetTotalCenterOfImpact))]
    internal static class AishiModifiedMdrAccuracy
    {
        [HarmonyPostfix]
        private static void AfterGetTotalCenterOfImpact(Weapon __instance, ref float __result)
        {
            if (__instance != null && IsbInsuranceCatalog.ModifiedMdrIds.Contains(__instance.StringTemplateId))
                __result *= .7f;
        }
    }
}
