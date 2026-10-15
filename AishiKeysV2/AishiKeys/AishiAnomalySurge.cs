using System;
using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
#if SPT41
using NativeBuff = EFT.HealthSystem.IStimulatorBuff;

#else
using NativeBuff = IPlayerBuff;
#endif
namespace AishiKeys
{
    [HarmonyPatch]
    internal static class AishiAnomalySurge
    {
        private static readonly Type Stimulator = AccessTools.Inner(typeof(ActiveHealthController), "Stimulator");
        private static readonly PropertyInfo ItemTemplate = AccessTools.Property(Stimulator, "ItemTemplateId");
        private static readonly PropertyInfo Controller = AccessTools.Property(Stimulator, "HealthController");
        private static MethodBase TargetMethod() => AccessTools.Method(Stimulator, 
#if SPT41
        "ProcessBuff", 
#else
            "method_7",
#endif
        new[] { typeof(NativeBuff), typeof(float) });
        private static bool Prefix(object __instance, NativeBuff __0, float __1)
        {
            if ((string)ItemTemplate.GetValue(__instance, null) != "f131c988930a86c7e5610c57" || __0.Settings.BuffType != EStimulatorBuffType.HealthRate || __0.Value >= 0)
                return true;
            AishiAnomalyInventory.DamageEachLimb(Controller.GetValue(__instance, null) as ActiveHealthController, -__0.Value * __1);
            return false;
        }
    }
}
