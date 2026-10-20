using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
#if !SPT41
using ItemIconCreator = GClass926;
using IconsHash = GClass928;
#endif

namespace AishiKeys
{
    [HarmonyPatch]
    internal static class AishiHammerheadStatus
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Item), nameof(Item.Name));
            yield return AccessTools.PropertyGetter(typeof(Item), nameof(Item.ShortName));
        }

        private static void Postfix(Item __instance, ref string __result)
        {
            if (AishiHammerheadPower.IsCharged(__instance))
                __result = GClass2348.Localized(__result, null) + " (Charged)";
        }
    }

    [HarmonyPatch(typeof(Item), "get_ItemInteractionButtons")]
    internal static class AishiHammerheadInventoryButtons
    {
        private static void Postfix(Item __instance, ref IEnumerable<EItemInfoButton> __result)
        {
            if (AishiHammerhead.IsHelmet(__instance))
                __result = __result.Where(button => button != EItemInfoButton.TurnOn && button != EItemInfoButton.TurnOff);
        }
    }

    [HarmonyPatch(typeof(ItemIconCreator), "RenderModel")]
    internal static class AishiHammerheadPreview
    {
        private static void Prefix(Item __0, ref ItemIconCreator.SpriteFactory __1)
        {
            if (!AishiHammerhead.IsHelmet(__0)) return;
            var capture = __1;
            __1 = async (model, pivot) =>
            {
                var states = model.GetComponentsInChildren<UnityEngine.Renderer>(true)
                    .Select(renderer => new { renderer, renderer.forceRenderingOff, renderer.shadowCastingMode }).ToArray();
                try
                {
                    foreach (var state in states)
                    {
                        state.renderer.forceRenderingOff = false;
                        state.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                    return await capture(model, pivot);
                }
                finally
                {
                    foreach (var state in states)
                    {
                        if (state.renderer == null) continue;
                        state.renderer.forceRenderingOff = state.forceRenderingOff;
                        state.renderer.shadowCastingMode = state.shadowCastingMode;
                    }
                }
            };
        }
    }
    [HarmonyPatch(typeof(ItemIconCreator), nameof(ItemIconCreator.GetItemIcon))]
    internal static class AishiHammerheadPreviewRefresh
    {
        private static readonly HashSet<int> Refreshed = new HashSet<int>();
        private static void Prefix(Item __0, ref bool __2)
        {
            if (AishiHammerhead.IsHelmet(__0) && Refreshed.Add(IconsHash.GetItemHash(__0))) __2 = true;
        }
    }

}
