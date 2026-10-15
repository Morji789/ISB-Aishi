using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace AishiKeys
{
    [HarmonyPatch]
    internal static class AishiBoneRestoration
    {
        internal const string Template = "778261046311c86a18968cfd";
        internal const string SingleTemplate = "282e86b6bc2955c6998fea9f";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Stimulator = typeof(ActiveHealthController).GetNestedType("Stimulator", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ItemTemplate = Stimulator.GetProperty("ItemTemplateId", Members);
        private static readonly PropertyInfo Controller = Stimulator.GetProperty("HealthController", Members);
        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg
        };
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return Stimulator.GetMethod("Started", Members);
            yield return Stimulator.GetMethod("UpdateWithSameOne", Members, null, new[] { typeof(float) }, null);
        }

        private static void Postfix(object __instance)
        {
            var template = (string)ItemTemplate.GetValue(__instance, null);
            if (template != Template && template != SingleTemplate)
                return;
            var health = Controller.GetValue(__instance, null) as ActiveHealthController;
            if (health == null || health.Player != null && !health.Player.IsYourPlayer)
                return;
            Restore(health.IsBodyPartDestroyed, part => health.RestoreBodyPart(part, .85f), template == SingleTemplate ? 1 : Parts.Length);
        }

        internal static void RestoreAll(Func<EBodyPart, bool> destroyed, Action<EBodyPart> restore)
        {
            Restore(destroyed, restore, Parts.Length);
        }

        internal static void Restore(Func<EBodyPart, bool> destroyed, Action<EBodyPart> restore, int limit)
        {
            foreach (var part in Parts)
                if (limit > 0 && destroyed(part))
                {
                    restore(part);
                    limit--;
                }
        }
    }
}
