using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace AishiKeys
{
    [HarmonyPatch]
    internal static class AishiAnomalousEffects
    {
        internal const string InertiaTemplate = "1800c994e11cfa35b423e8ed";
        private static readonly Type Stimulator = AccessTools.Inner(typeof(ActiveHealthController), "Stimulator");
        private static readonly PropertyInfo ItemTemplate = AccessTools.Property(Stimulator, "ItemTemplateId");
        private static readonly PropertyInfo Controller = AccessTools.Property(Stimulator, "HealthController");
        private static MethodBase TargetMethod() => AccessTools.Method(Stimulator, "Started");
        private static void Postfix(object __instance)
        {
            var id = (string)ItemTemplate.GetValue(__instance, null);
            var owner = (Controller.GetValue(__instance, null) as ActiveHealthController)?.Player;
            if (owner == null || owner.MovementContext == null)
                return;
            var local = AishiAnomalyInventory.Local;
            if (owner.IsYourPlayer && local != null && local.IsActive && ReferenceEquals(owner, local.Owner))
                local.Extended.Inject(id);
            if (id != InertiaTemplate)
                return;
            var effect = owner.GetComponent<AishiInertiaEffect>() ?? owner.gameObject.AddComponent<AishiInertiaEffect>();
            effect.Apply(owner.MovementContext);
        }
    }

    internal sealed class AishiInertiaEffect : MonoBehaviour
    {
        private const Player.ESpeedLimit Limit = (Player.ESpeedLimit)13717;
        private MovementContext movement;
        private float expires;
        internal void Apply(MovementContext context)
        {
            movement = context;
            expires = Time.realtimeSinceStartup + 300f;
            movement.RemoveStateSpeedLimit(Limit);
            movement.AddStateSpeedLimit(.8f, Limit);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup >= expires)
                Destroy(this);
        }

        private void OnDestroy()
        {
            movement?.RemoveStateSpeedLimit(Limit);
            movement = null;
        }
    }
}
