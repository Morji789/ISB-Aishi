using System;
using System.Linq;
using HarmonyLib;
using AishiKeysPro;

namespace AishiKeys
{
    internal static class AishiPatchBootstrap
    {
        internal static bool IsFinance(Type type) => type.Name.StartsWith("AishiInsurance", StringComparison.Ordinal) || type.Name.StartsWith("AishiRepair", StringComparison.Ordinal) || type.Name.StartsWith("AishiTradingSpend", StringComparison.Ordinal);
        internal static void EnableGroup(bool finance)
        {
            var harmony = new Harmony(finance ? "com.samc137.aishi.dollar-ui" : "com.samc137.aishi.gameplay");
            var errors = 0;
            foreach (var type in typeof(AishiPatchBootstrap).Assembly.GetTypes().Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0 && IsFinance(type) == finance).OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception error)
                {
                    errors++;
                    AishiKeysMod.Logger.LogError("Aishi patch registration failed for " + type.FullName + ": " + error);
                }
            }

            if (errors > 0)
                throw new InvalidOperationException("Aishi patch registration failed for " + errors + " component(s).");
        }
    }
}
