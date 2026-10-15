using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using DamageInfoStruct = EFT.Ballistics.DamageInfo;
using Factory = EFT.ItemFactory;
using GridSearch = EFT.InventoryLogic.InventoryEquipmentExtension;
using ItemActions = EFT.InventoryLogic.ItemManipulator;
#else
using Factory = ItemFactoryClass;
using GridSearch = GClass3372;
using ItemActions = InteractionsHandlerClass;
#endif
using Comfort.Common;

namespace AishiKeys
{
    internal sealed class AishiAnomalyS2
    {
        internal const string Tea = "d94f4a94edf1050b40c93cdb", Cigarettes = "eee72a686853f282c7cd01da", Coffee = "25dd7397974f7d7940ad9742", Pickles = "68c80d9fb3ef768bbe58a072", Paracord = "d88925f5ddd4cd8795d5c9bd", Press = "a2c16b836b441dc953652bd6", Welding = "571e1ce5aa8a32c10b70d206", Lighter = "3ddc22a4b7e5ba67ee38edf9", Ledx = "b7e0639e756abd3bbb872c2c", Defibrillator = "337052e7f499ba2f52dfa089", Egg = "397495f6cda093c895814c0f", Badge = "e57cfb4c1b58a40f9032db70", Euros = "569668774bdc2da2298b4568", Roubles = "5449016a4bdc2d6f028b456f";
        private readonly AishiAnomalyInventory state;
        private float teaSeconds, teaRemaining, pickleSeconds, cordSeconds, lighterSeconds, bleedSeconds, eggSeconds, badgeSeconds;
        private float protectedUntil, nextEffects, nextDoors;
        private int pendingEuros, pendingRoubles;
        private bool ledxActivated, ledxSuppressed;
        private object berserk, tunnel, fatigue, teaEffect, teaPainkiller;
        private sealed class EffectApi
        {
            internal MethodInfo Add, Find, Remove, Extend, Store, Residue;
            internal PropertyInfo TimeLeft;
        }

        private static readonly Dictionary<string, EffectApi> EffectApis = new Dictionary<string, EffectApi>();
#if SPT41
        private const string ResidueMethod = "ResidueEffect";
#else
        private const string ResidueMethod = "method_16";
#endif
        private Door[] doors;
        private readonly Dictionary<Door, Collider[]> doorColliders = new Dictionary<Door, Collider[]>();
        [ThreadStatic]
        internal static bool ExplicitResourceDrain;
        internal AishiAnomalyS2(AishiAnomalyInventory owner)
        {
            state = owner;
        }

        internal bool BlocksJump => state.Carries(Welding);
        internal float SpeedFactor => state.Carries(Welding) ? .5f : 1f;
        internal float JumpFactor => state.Carries(Paracord) ? 1.5f : 1f;
        internal bool FreezeMetabolism => state.Carries(Pickles);
        internal bool Invulnerable => Time.realtimeSinceStartup < protectedUntil;

        internal static bool Advance(ref float elapsed, float seconds, float interval)
        {
            elapsed += seconds;
            if (elapsed < interval)
                return false;
            elapsed %= interval;
            return true;
        }

        internal static void Drain(ActiveHealthController health, float amount)
        {
            bool previous = ExplicitResourceDrain;
            ExplicitResourceDrain = true;
            try
            {
                health.ChangeEnergy(-amount);
                health.ChangeHydration(-amount);
            }
            finally
            {
                ExplicitResourceDrain = previous;
            }
        }

        private static EffectApi Api(string name)
        {
            if (EffectApis.TryGetValue(name, out var api))
                return api;
            var type = typeof(ActiveHealthController).GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
            var methods = typeof(ActiveHealthController).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            api = new EffectApi
            {
                Add = methods.Single(m => m.Name == "AddEffect" && m.IsGenericMethodDefinition && m.GetParameters().Length == 6).MakeGenericMethod(type),
                Find = methods.Single(m => m.Name == "FindExistingEffect" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1).MakeGenericMethod(type),
                Remove = type.GetMethod("ForceRemove", BindingFlags.Public | BindingFlags.Instance),
                Extend = type.GetMethod("AddWorkTime", BindingFlags.Public | BindingFlags.Instance),
                TimeLeft = type.GetProperty("TimeLeft", BindingFlags.Public | BindingFlags.Instance),
                Store = type.GetMethod("StoreValues", BindingFlags.Public | BindingFlags.Instance),
                Residue = methods.Single(m => m.Name == ResidueMethod && m.IsGenericMethodDefinition && m.GetParameters().Length == 1).MakeGenericMethod(type)
            };
            EffectApis[name] = api;
            return api;
        }

        private object Add(string name, EBodyPart part, float? duration, Action<object> initialize = null) => Api(name).Add.Invoke(state.Health, new object[] { part, (float? )0f, duration, (float? )0f, (float? )1f, initialize });
        private void Maintain(string name, bool active, ref object owned, EBodyPart part)
        {
            var api = Api(name);
            if (!active)
            {
                if (owned != null)
                    api.Remove.Invoke(owned, null);
                owned = null;
                return;
            }

            var existing = api.Find.Invoke(state.Health, new object[] { (EBodyPart? )part });
            if (owned != null && ReferenceEquals(existing, owned))
            {
                if ((float)api.TimeLeft.GetValue(owned, null) < 2f)
                    api.Extend.Invoke(owned, new object[] { (float? )3f, true });
                return;
            }

            owned = null;
            if (existing == null)
                owned = Add(name, part, 3f);
        }

        internal void UpdatePresentation()
        {
            float now = Time.realtimeSinceStartup;
            if (state.Carries(Ledx))
                ledxActivated = true;
            if (state.Carries(Pickles))
            {
                var stamina = state.Owner.Physical.Stamina;
                stamina.UpdateStamina(stamina.TotalCapacity);
                var hands = state.Owner.Physical.HandsStamina;
                hands.UpdateStamina(hands.TotalCapacity);
            }

            if (now >= nextEffects)
            {
                nextEffects = now + .5f;
                ledxSuppressed = state.Owner.InventoryController.Inventory.Equipment.GetAllItems().Any(i => i.TemplateId == Ledx && AishiAnomalyContainment.IsSuppressed(i));
                state.RunSection("Berserk", () => Maintain("Berserk", state.Carries(Coffee) || ledxActivated && !ledxSuppressed, ref berserk, EBodyPart.Head));
                state.RunSection("TunnelVision", () => Maintain("TunnelVision", state.Carries(Cigarettes), ref tunnel, EBodyPart.Head));
                state.RunSection("Fatigue", () => Maintain("ChronicStaminaFatigue", state.Carries(Cigarettes), ref fatigue, EBodyPart.Chest));
                if (!state.Carries(Tea))
                    StopTea();
            }

            if (now >= nextDoors && state.Carries(Press))
            {
                nextDoors = now + .2f;
                UnlockNearDoor();
            }
        }

        internal void Tick(float seconds)
        {
            bool tea = state.Carries(Tea);
            if (tea && Advance(ref teaSeconds, seconds, 300f))
                teaRemaining = Mathf.Max(0f, 60f - teaSeconds);
            else
                teaRemaining = Mathf.Max(0f, teaRemaining - seconds);
            if (tea && teaRemaining > 0f)
            {
                if (teaEffect == null)
                {
                    teaEffect = Add("Stimulator", EBodyPart.Head, teaRemaining, e => Api("Stimulator").Store.Invoke(e, new object[] { "AishiKTEPropital", Tea, EBodyPart.Head }));
                    if (Api("PainKiller").Find.Invoke(state.Health, new object[] { (EBodyPart? )EBodyPart.Head }) == null)
                        teaPainkiller = Add("PainKiller", EBodyPart.Head, teaRemaining);
                }
            }

            if (teaRemaining <= 0f)
                StopTea();
            if (state.Carries(Pickles))
            {
                if (Advance(ref pickleSeconds, seconds, 60f))
                    DamageRandom(15f, false);
            }

            if (state.Carries(Paracord) && Advance(ref cordSeconds, seconds, 360f))
                Drain(state.Health, 50f);
            if (state.Carries(Lighter) && Advance(ref lighterSeconds, seconds, 300f))
                DamageRandom(25f, true);
            if (state.Carries(Ledx))
            {
                Heal(5f * seconds);
                state.Health.ChangeEnergy(5f * seconds);
                state.Health.ChangeHydration(5f * seconds);
                if (Advance(ref bleedSeconds, seconds, 300f))
                {
                    var shuffled = AishiAnomalyInventory.BodyParts.OrderBy(p => UnityEngine.Random.value).Take(6);
                    foreach (var part in shuffled)
                        Add("HeavyBleeding", part, null);
                }
            }

            if (Invulnerable)
            {
                RestoreFatalParts();
                Heal(15f * seconds);
            }

            if (state.Carries(Egg))
            {
                if (Advance(ref eggSeconds, seconds, 60f))
                    pendingEuros += 2;
                if (pendingEuros > 0)
                    GrantCurrency(Egg, Euros, ref pendingEuros, 2);
            }

            if (state.Carries(Badge))
            {
                if (Advance(ref badgeSeconds, seconds, 60f))
                    pendingRoubles += 1000;
                if (pendingRoubles > 0)
                    GrantCurrency(Badge, Roubles, ref pendingRoubles, 1000);
            }
        }

        private void StopTea()
        {
            if (teaEffect != null)
                Api("Stimulator").Remove.Invoke(teaEffect, null);
            teaEffect = null;
            if (teaPainkiller != null)
                Api("PainKiller").Remove.Invoke(teaPainkiller, null);
            teaPainkiller = null;
        }

        private void Heal(float amount)
        {
            foreach (var part in AishiAnomalyInventory.BodyParts)
                if (!state.Health.IsBodyPartDestroyed(part))
                    state.Health.ChangeHealth(part, amount, default(DamageInfoStruct));
        }

        private void DamageRandom(float amount, bool excludeHead)
        {
            var parts = AishiAnomalyInventory.BodyParts.Where(p => (!excludeHead || p != EBodyPart.Head) && !state.Health.IsBodyPartDestroyed(p) && state.Health.GetBodyPartHealth(p, false).Current > 0f).ToArray();
            if (parts.Length == 0)
                return;
            var selected = parts[UnityEngine.Random.Range(0, parts.Length)];
            state.Health.ApplyDamage(selected, amount, new DamageInfoStruct { DamageType = EDamageType.Poison, Damage = amount });
        }

        internal bool PreventFatal()
        {
            if (Invulnerable)
                return true;
            var item = state.CarriedItems.FirstOrDefault(i => i.TemplateId == Defibrillator && i.GetItemComponent<ResourceComponent>()?.Value > 0f);
            if (item == null)
                return false;
            var resource = item.GetItemComponent<ResourceComponent>();
            resource.Value = 0f;
            protectedUntil = Time.realtimeSinceStartup + 15f;
            AishiAnomalyDuck.Changed(state.Owner, item, 0f);
            RestoreFatalParts();
            return true;
        }

        private void RestoreFatalParts()
        {
            foreach (var part in AishiAnomalyInventory.BodyParts)
            {
                if (state.Health.IsBodyPartDestroyed(part) || state.Health.GetBodyPartHealth(part, false).Current <= 0f)
                {
                    if (part == EBodyPart.Head || part == EBodyPart.Chest)
                        state.Health.FullRestoreBodyPart(part);
                    else
                        state.Health.RestoreBodyPart(part, 1f);
                }

                var fractureApi = Api("Fracture");
                var fracture = fractureApi.Find.Invoke(state.Health, new object[] { part });
                if (fracture != null)
                    fractureApi.Remove.Invoke(fracture, null);
            }
        }

        internal bool PreventDamage(EBodyPart part, float amount)
        {
            if (Invulnerable)
                return true;
            return (part == EBodyPart.Head || part == EBodyPart.Chest) && amount >= state.Health.GetBodyPartHealth(part, false).Current && PreventFatal();
        }

        internal static bool NaturallyLocked(Door door) => door != null && door.InitialDoorState == EDoorState.Locked;
        private void UnlockNearDoor()
        {
            var item = state.CarriedItems.FirstOrDefault(i => i.TemplateId == Press && i.GetItemComponent<ResourceComponent>()?.Value > 0f);
            if (item == null)
                return;
            if (doors == null)
                doors = Resources.FindObjectsOfTypeAll<Door>().Where(d => d != null && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded).ToArray();
            foreach (var door in doors)
            {
                if (!NaturallyLocked(door) || door.DoorState != EDoorState.Locked)
                    continue;
                if (!doorColliders.TryGetValue(door, out var colliders))
                    doorColliders[door] = colliders = door.GetComponentsInChildren<Collider>();
                if (!WithinDoorRadius(state.Owner, door, colliders, .3f))
                    continue;
                if (AishiAnomalyPressNetwork.TryUnlock(state.Owner, item, door))
                    return;
                if (!WorldInteractionUtils.TryApplyAuthoritativeDoorState(door, (int)EDoorState.Shut))
                    return;
                var resource = item.GetItemComponent<ResourceComponent>();
                resource.Value = Mathf.Max(0f, resource.Value - 1f);
                item.RaiseRefreshEvent(false, true);
                AishiAnomalyDuck.Changed(state.Owner, item, resource.Value);
                return;
            }
        }

        internal static bool WithinDoorRadius(Player owner, Door door, Collider[] colliders, float radius)
        {
            var position = owner.Position + Vector3.up;
            if ((position - door.transform.position).sqrMagnitude > 25f)
                return false;
            if (colliders.Length == 0)
                return (position - door.transform.position).sqrMagnitude <= radius * radius;
            foreach (var collider in colliders)
                if (collider != null && collider.enabled && (collider.ClosestPoint(position) - position).sqrMagnitude <= radius * radius)
                    return true;
            return false;
        }

        private void GrantCurrency(string source, string tpl, ref int pending, int amount)
        {
            if (AishiAnomalyCurrencyNetwork.TryGrant(state.Owner, source, tpl, amount, out int granted))
            {
                pending = Math.Max(0, pending - granted);
                return;
            }

            var controller = state.Owner.InventoryController;
            foreach (var item in state.CarriedItems)
            {
                if (item.TemplateId != tpl || item.StackObjectsCount > item.StackMaxSize - amount)
                    continue;
                int quantity = Math.Min(pending, item.StackMaxSize - item.StackObjectsCount);
                item.StackObjectsCount += quantity;
                pending -= quantity;
                item.RaiseRefreshEvent(false, true);
                if (pending == 0)
                    return;
            }

            var currency = Singleton<Factory>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), tpl, null);
            currency.StackObjectsCount = Math.Min(currency.StackMaxSize, pending);
            currency.SpawnedInSession = true;
            var address = GridSearch.GetPrioritizedGridsForLoot(controller.Inventory.Equipment, currency).Select(g => g.FindLocationForItem(currency)).FirstOrDefault(p => p != null && !ItemActions.Add(currency, p, controller, true).Failed);
            if (address == null)
                return;
            var added = ItemActions.Add(currency, address, controller, false);
            if (added.Failed)
                return;
            added.Value.RaiseEvents(controller, CommandStatus.Succeed);
            pending -= currency.StackObjectsCount;
        }

        internal void Dispose()
        {
            StopTea();
            if (berserk != null)
                Api("Berserk").Remove.Invoke(berserk, null);
            if (tunnel != null)
                Api("TunnelVision").Remove.Invoke(tunnel, null);
            if (fatigue != null)
                Api("ChronicStaminaFatigue").Remove.Invoke(fatigue, null);
            doors = null;
            doorColliders.Clear();
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), "ChangeHealth")]
    internal static class AishiAnomalyDefibrillatorHealth
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ActiveHealthController __instance, EBodyPart __0, float __1)
        {
            var state = AishiAnomalyInventory.Local;
            return __1 >= 0f || state == null || !state.IsActive || !ReferenceEquals(state.Health, __instance) || !state.aishiS2.PreventDamage(__0, -__1);
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage")]
    internal static class AishiAnomalyDefibrillatorDamage
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ActiveHealthController __instance, EBodyPart __0, float __1, ref DamageInfoStruct __2, ref float __result)
        {
            var state = AishiAnomalyInventory.Local;
            if (state == null || !state.IsActive || !ReferenceEquals(state.Health, __instance) || __1 <= 0f || !state.aishiS2.PreventDamage(__0, __1))
                return true;
            __2.Damage = 0f;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), "Kill")]
    internal static class AishiAnomalyDefibrillatorDeath
    {
        private static bool Prefix(ActiveHealthController __instance)
        {
            var state = AishiAnomalyInventory.Local;
            return state == null || !state.IsActive || !ReferenceEquals(state.Health, __instance) || !state.aishiS2.PreventFatal();
        }
    }

    [HarmonyPatch]
    internal static class AishiAnomalyPickleMetabolism
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ActiveHealthController), "ChangeEnergy");
            yield return AccessTools.Method(typeof(ActiveHealthController), "ChangeHydration");
        }

        private static bool Prefix(ActiveHealthController __instance, float __0)
        {
            var state = AishiAnomalyInventory.Local;
            return __0 >= 0f || AishiAnomalyS2.ExplicitResourceDrain || state == null || !state.IsActive || !ReferenceEquals(state.Health, __instance) || !state.aishiS2.FreezeMetabolism;
        }
    }
}
