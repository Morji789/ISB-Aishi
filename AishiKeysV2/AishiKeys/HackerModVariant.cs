using AishiKeysPro;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace AishiKeys
{
    public static class HackerModVariant
    {
        private const string HackerModGuid = "com.manimal.hackermod";
        private const string MeuItemId = "6a321d846b38e922175d1878";
        public static void TryInject()
        {
            if (!Chainloader.PluginInfos.ContainsKey(HackerModGuid))
                return;
            var hackerConstantsType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == "Manimal.HackerMod.HackerConstants");
            if (hackerConstantsType == null)
                return;
            var field = hackerConstantsType.GetField("AllHackerDeviceTpls", BindingFlags.Public | BindingFlags.Static);
            if (field == null)
                return;
            var current = field.GetValue(null) as string[];
            if (current == null)
                return;
            if (Array.IndexOf(current, MeuItemId) < 0)
            {
                var novo = new string[current.Length + 1];
                current.CopyTo(novo, 0);
                novo[current.Length] = MeuItemId;
                field.SetValue(null, novo);
            }

            var getCanvasRotation = hackerConstantsType.GetMethod("GetCanvasRotation", BindingFlags.Public | BindingFlags.Static);
            if (getCanvasRotation == null)
                return;
            var harmony = new Harmony("com.samc137.aishi.hackercolab");
            harmony.Patch(getCanvasRotation, postfix: new HarmonyMethod(typeof(HackerModVariant).GetMethod(nameof(GetCanvasRotationPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void GetCanvasRotationPostfix(string tpl, ref float __result)
        {
            if (tpl == MeuItemId)
                __result = 180f;
        }
    }
}
