using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace AishiKeys
{
    [HarmonyPatch(typeof(HandbookFilterPanel), "PreInit")]
    internal static class AishiHandbookInitialization
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo Handbook = typeof(HandbookFilterPanel).GetFields(Fields).First(f => f.FieldType.Name == "HandbookClass" || f.FieldType.FullName == "EFT.HandBook.Handbook");
        private static readonly FieldInfo Tabs = AccessTools.Field(typeof(FilterPanel), "_filterTabs");
        private static readonly MethodInfo Awake = AccessTools.Method(typeof(HandbookFilterPanel), "Awake");
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(HandbookFilterPanel __instance)
        {
            if (Handbook.GetValue(__instance) != null)
                return;
            var singleton = typeof(Singleton<>).MakeGenericType(Handbook.FieldType);
            var current = singleton.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            if (current == null)
                return;
            var list = typeof(HandbookFilterPanel).GetFields(Fields).FirstOrDefault(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>));
            if (list?.GetValue(__instance) != null && Tabs.GetValue(__instance)is Array currentTabs && currentTabs.Length > 0 && AccessTools.Field(typeof(FilterPanel), "_tabs")?.GetValue(__instance) != null)
            {
                Handbook.SetValue(__instance, current);
                return;
            }

            if (Tabs.GetValue(__instance)is IEnumerable oldTabs)
                foreach (var entry in oldTabs)
                    if (entry is Component tab && tab != null && tab.transform.IsChildOf(__instance.transform))
                        UnityEngine.Object.DestroyImmediate(tab.gameObject);
            foreach (var field in typeof(HandbookFilterPanel).GetFields(Fields))
                if (field.GetValue(__instance)is IDictionary dictionary)
                    dictionary.Clear();
            Tabs.SetValue(__instance, null);
            AccessTools.Field(typeof(FilterPanel), "_tabs")?.SetValue(__instance, null);
            Awake.Invoke(__instance, null);
        }
    }
}
