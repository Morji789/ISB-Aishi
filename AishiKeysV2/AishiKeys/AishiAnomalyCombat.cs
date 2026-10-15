using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using DamageInfoStruct = EFT.Ballistics.DamageInfo;
using JumpState = EFT.JumpPlayerState;
#else
using JumpState = JumpStateClass;

#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(JumpState), "Enter")]
    internal static class AishiAnomalyJump
    {
        private static readonly FieldInfo Lift = AccessTools.Field(typeof(JumpState), 
#if SPT41
            "_liftForce");
#else
        "Vector3_1");
#endif
        private static void Postfix(JumpState __instance)
        {
            var state = AishiAnomalyInventory.Local;
            if (state == null || !state.IsActive || !ReferenceEquals(__instance.MovementContext, state.Owner.MovementContext))
                return;
            var force = (Vector3)Lift.GetValue(__instance);
            force.y *= Mathf.Sqrt((state.HasSneaker ? 1.5f : 1f) * state.Extended.JumpFactor);
            Lift.SetValue(__instance, force);
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage")]
    internal static class AishiKunaiDamage
    {
        private static void Prefix(ref float __1, ref DamageInfoStruct __2)
        {
            if (__1 <= 0 || __2.OverDamageFrom.HasValue)
                return;
            var attacker = __2.Player?.iPlayer as Player;
            if (attacker == null || attacker.IsAI)
                return;
            var local = AishiAnomalyInventory.Local;
            bool empowered = local != null && ReferenceEquals(attacker, local.Owner) ? local.IsActive && local.HasKunai : CarriesKunai(attacker);
            if (!empowered)
                return;
            __1 *= 1.3f;
            __2.Damage *= 1.3f;
        }

        private static bool CarriesKunai(Player player)
        {
            var root = player.Profile?.Inventory?.Equipment;
            if (root == null)
                return false;
            var pending = new Stack<Item>();
            var visited = new HashSet<Item>();
            pending.Push(root);
            int budget = 4096;
            while (pending.Count > 0 && --budget > 0)
            {
                var item = pending.Pop();
                if (item == null || !visited.Add(item))
                    continue;
                if (AishiAnomalyContainment.IsCase(item))
                    continue;
                if (item.TemplateId == AishiAnomalyInventory.Kunai)
                    return true;
                if (!(item is CompoundItem container))
                    continue;
                if (container.Grids != null)
                    foreach (var grid in container.Grids)
                        if (grid != null)
                            foreach (var child in grid.Items)
                                if (child != null && pending.Count < 4096)
                                    pending.Push(child);
                if (container.Slots != null)
                    foreach (var slot in container.Slots)
                        if (slot?.ContainedItem != null && pending.Count < 4096)
                            pending.Push(slot.ContainedItem);
            }

            return false;
        }
    }
}
