using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using FastAccess = EFT.InventoryLogic.FastAccess;

namespace AishiKeys
{
    internal static class AishiAnomalyQuickSlots
    {
        private static readonly HashSet<string> Injectors = new HashSet<string>
        {
            "b27508815b9307ccd1bffa36",
            "1800c994e11cfa35b423e8ed",
            "36be503998aadd53701c57e3",
            "f131c988930a86c7e5610c57",
            "a8d639d499ac25709f630d2c",
            "922129d70c57a265f61ee8b1",
            "d180bb453e8bb2027c5fa540",
            "51574a824fe10af4518191af",
            "0056a97d873bba252113c74b"
        };
        internal static bool IsPassive(Item item) => item != null && AishiInventoryDescriptions.IsAnomaly(item.TemplateId) && !Injectors.Contains(item.TemplateId);
        internal static bool CanQuickUse(FastAccess access, EBoundItem key) => !IsPassive(access?.GetBoundItem(key));
        internal static void ClearPassiveBindings(FastAccess access)
        {
            var bindings = access?.BoundItems;
            if (bindings == null)
                return;
            var remove = new List<EBoundItem>();
            foreach (var binding in bindings)
                if (IsPassive(binding.Value))
                    remove.Add(binding.Key);
            foreach (var key in remove)
                bindings.Remove(key);
        }
    }

    [HarmonyPatch(typeof(InventoryController), "IsAtBindablePlace")]
    internal static class AishiAnomalyBinding
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Item __0, ref bool __result)
        {
            if (AishiAnomalyQuickSlots.IsPassive(__0))
                __result = false;
        }
    }

    [HarmonyPatch]
    internal static class AishiAnomalySavedBindings
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(FastAccess).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static void Postfix(FastAccess __instance) => AishiAnomalyQuickSlots.ClearPassiveBindings(__instance);
    }

    [HarmonyPatch(typeof(Player), "SetQuickSlotItem")]
    internal static class AishiAnomalyQuickUse
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance, EBoundItem __0, Callback<IHandsController> __1)
        {
            if (AishiAnomalyQuickSlots.CanQuickUse(__instance.InventoryController?.FastAccess, __0))
                return true;
            __1?.Invoke(null);
            return false;
        }
    }
}
