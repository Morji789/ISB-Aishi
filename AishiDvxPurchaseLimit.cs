using System;
using HarmonyLib;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Aishi;
[HarmonyPatch(typeof(TraderHelper), "AddTraderPurchasesToPlayerProfile")]
internal static class AishiDvxPurchaseLimit
{
    [ThreadStatic]
    internal static bool ExactLimit;
    private static void Prefix(Item __2, out bool __state)
    {
        __state = ExactLimit;
        ExactLimit = __2?.Id.ToString() == "4a242c6a3685ba67964570ac" && __2.Template.ToString() == "6a426637ddc63098d100ae67";
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        ExactLimit = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(TraderHelper), "GetAccountTypeAdjustedTraderPurchaseLimit")]
internal static class AishiDvxEditionLimit
{
    private static void Postfix(ref double __result)
    {
        if (AishiDvxPurchaseLimit.ExactLimit)
            __result = 120;
    }
}
