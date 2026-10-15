using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine.UI;

namespace AishiKeys
{
    [HarmonyPatch]
    internal static class AishiSwapHighlightCleanup
    {
        private static MethodInfo getSecondHighlight;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DraggedItemView), "Kill");
            yield return AccessTools.Method(typeof(DraggedItemView), "OnDisable");
        }

        private static void Prefix()
        {
            if (getSecondHighlight == null)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UIFixes.SwapPatches", false)).FirstOrDefault(t => t != null);
                if (type == null)
                    return;
                getSecondHighlight = AccessTools.Method(type, "GetSecondHighlight", new[] { typeof(bool) });
            }

            if (getSecondHighlight == null)
                return;
            var image = getSecondHighlight.Invoke(null, new object[] { false }) as Image;
            if (image != null)
                image.gameObject.SetActive(false);
        }
    }
}
