using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using TransitAccessController = TransitInteractionControllerAbstractClass;

namespace AishiKeys
{
    public sealed class AishiTransitKeycardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TransitAccessController), "method_11", new[] { typeof(Player), typeof(int), typeof(string).MakeByRefType() });
        }

        [PatchPostfix]
        private static void Postfix(TransitAccessController __instance, Player __0, int __1, ref string __2, bool __result)
        {
            if (!__result || string.IsNullOrEmpty(__2) || __0 == null)
                return;
            if (!__instance.Dictionary_0.TryGetValue(__1, out var point) || point == null)
                return;
            foreach (Item item in __0.InventoryController.Inventory.GetPlayerItems((EPlayerItems)2))
            {
                if (!string.Equals(item.Id, __2, StringComparison.Ordinal))
                    continue;
                if (AishiTransitKeycardPolicy.IsReusable(point.parameters.target, item.StringTemplateId))
                {
                    __2 = string.Empty;
                }

                return;
            }
        }
    }
}
