using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using ItemFactoryNative = EFT.ItemFactory;
using GridSearch = EFT.InventoryLogic.InventoryEquipmentExtension;
using ItemActions = EFT.InventoryLogic.ItemManipulator;
using DamageInfoStruct = EFT.Ballistics.DamageInfo;
using CameraNative = EFT.CameraControl.CameraManager;
#else
using ItemFactoryNative = ItemFactoryClass;
using GridSearch = GClass3372;
using ItemActions = InteractionsHandlerClass;
using CameraNative = CameraClass;

#endif
namespace AishiKeys
{
    internal sealed class AishiAnomalyExtended
    {
        internal const string Tape = "45e6cfe53aabe0b5e846cad5", Teapot = "9df4b9812799170d2f7295f7", Lion = "57d2c307a64e179e8995e061", Prokill = "90c688feeba7c89aa1a3c8a7", Ring = "4a2e4aaef50cda01112c41b2", Floppy = "73cc246044b1d66dd70aedb7", Bitcoin = "06f0d5bdc44c4ddcd0759d31", Dollars = "5696686a4bdc2da3298b456a";
        private readonly AishiAnomalyInventory state;
        private readonly Dictionary<EBodyPart, HealthValue> healthValues = new Dictionary<EBodyPart, HealthValue>();
        private readonly Dictionary<EBodyPart, float> bonuses = new Dictionary<EBodyPart, float>();
        private readonly List<object> effects = new List<object>();
        private static readonly Dictionary<string, MethodInfo> AddEffects = new Dictionary<string, MethodInfo>();
        private float rushUntil, panicUntil, rushStarted, panicStarted, thermalUntil, nightUntil;
        private float prokillSeconds, bitcoinSeconds, rushDrainSeconds, nextHealthBinding, appliedSpeed = 1f;
        private int pendingDollars;
        private bool boundHealth;
        private readonly AishiAnomalyVision vision;
        internal AishiAnomalyExtended(AishiAnomalyInventory owner)
        {
            state = owner;
            vision = new AishiAnomalyVision(owner.Owner);
        }

        internal bool BlocksJump => state.Carries(Tape) || state.aishiS2.BlocksJump;
        internal bool ProtectsDamage => state.Carries(Prokill);
        internal float SpeedFactor => (state.Carries(Lion) ? .5f : 1f) * (Time.time < panicUntil ? .5f : 1f) * (Time.time < rushUntil ? 1.4f : 1f) * state.aishiS2.SpeedFactor;
        internal float JumpFactor => (Time.time < rushUntil ? 1.2f : 1f) * state.aishiS2.JumpFactor;

        internal void Inject(string id)
        {
            float now = Time.time;
            switch (id)
            {
                case "a8d639d499ac25709f630d2c":
                    thermalUntil = float.PositiveInfinity;
                    AddEffect("Pain", 86400);
                    break;
                case "922129d70c57a265f61ee8b1":
                    AddEffect("PainKiller", 86400);
                    break;
                case "d180bb453e8bb2027c5fa540":
                    rushStarted = now;
                    rushUntil = now + 300f;
                    rushDrainSeconds = 0;
                    break;
                case "51574a824fe10af4518191af":
                    nightUntil = float.PositiveInfinity;
                    AddEffect("Pain", 86400);
                    break;
                case "0056a97d873bba252113c74b":
                    panicStarted = now;
                    panicUntil = now + 30f;
                    AddEffect("Pain", 30);
                    AddEffect("PanicEffect", 30);
                    break;
            }

            UpdatePresentation();
        }

        private void AddEffect(string name, float duration)
        {
            if (!AddEffects.TryGetValue(name, out var method))
            {
                var type = AccessTools.Inner(typeof(ActiveHealthController), name);
                method = typeof(ActiveHealthController).GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == "AddEffect" && m.IsGenericMethodDefinition && m.GetParameters().Length == 6).MakeGenericMethod(type);
                AddEffects[name] = method;
            }

            var effect = method.Invoke(state.Health, new object[] { EBodyPart.Head, (float? )0, (float? )duration, (float? )0, (float? )1, null });
            if (effect != null)
            {
                if (name == "Pain")
                {
                    float left = (float)AccessTools.Property(effect.GetType(), "TimeLeft").GetValue(effect, null);
                    if (left < duration)
                        AccessTools.Method(effect.GetType(), "AddWorkTime").Invoke(effect, new object[] { (float? )duration, true });
                    AccessTools.Method(effect.GetType(), "SetStrength").Invoke(effect, new object[] { 1f });
                }

                if (!effects.Contains(effect))
                    effects.Add(effect);
            }
        }

        internal void UpdatePresentation()
        {
            if (!state.Carries(Teapot) && bonuses.Count > 0)
                ResetHealthBonuses();
            float speed = SpeedFactor;
            var movement = state.Owner?.MovementContext;
            if (movement != null && !Mathf.Approximately(speed, appliedSpeed))
            {
                float previous = appliedSpeed;
                appliedSpeed = speed;
                movement.SetCharacterMovementSpeed(movement.CharacterMovementSpeed * speed / previous, true);
            }

            vision.Update(Time.time < thermalUntil, Time.time < nightUntil || state.Carries(Ring));
        }

        internal void Tick(float seconds)
        {
            AishiAnomalyDuck.Inspect(state);
            float now = Time.time;
            float rushSeconds = Mathf.Max(0f, Mathf.Min(now, rushUntil) - Mathf.Max(now - seconds, rushStarted));
            float panicSeconds = Mathf.Max(0f, Mathf.Min(now, panicUntil) - Mathf.Max(now - seconds, panicStarted));
            bool teapot = state.Carries(Teapot), rush = rushSeconds > 0, duck = AishiAnomalyDuck.Active(state) != null;
            if ((teapot || rush) && !boundHealth && now >= nextHealthBinding)
            {
                nextHealthBinding = now + 2f;
                BindHealth();
            }

            if (teapot || rush || duck)
            {
                foreach (var part in AishiAnomalyInventory.BodyParts)
                {
                    if (state.Health.IsBodyPartDestroyed(part))
                        continue;
                    if (teapot && healthValues.TryGetValue(part, out var value) && value.AtMaximum)
                    {
                        bonuses.TryGetValue(part, out float bonus);
                        float extra = Mathf.Min(seconds, 50f - bonus);
                        if (extra > 0)
                        {
                            value.Value.Maximum += extra;
                            bonuses[part] = bonus + extra;
                            state.Health.ChangeHealth(part, extra, default(DamageInfoStruct));
                        }
                    }

                    state.Health.ChangeHealth(part, (teapot ? seconds : 0f) + (duck ? seconds : 0f) + rushSeconds * 1.5f, default(DamageInfoStruct));
                }
            }

            if (duck)
            {
                var stamina = state.Owner.Physical.Stamina;
                stamina.UpdateStamina(Mathf.Min(stamina.TotalCapacity, stamina.Current + seconds));
            }

            AishiAnomalyDuck.Update();
            if (state.Carries(Prokill))
            {
                prokillSeconds += seconds;
                while (prokillSeconds >= 60f)
                {
                    prokillSeconds -= 60f;
                    Drain(10f);
                }
            }

            if (rush)
            {
                rushDrainSeconds += rushSeconds;
                while (rushDrainSeconds >= 5f)
                {
                    rushDrainSeconds -= 5f;
                    Drain(5f);
                }
            }

            if (panicSeconds > 0f)
                Drain(50f * panicSeconds);
            if (state.Carries(Bitcoin))
            {
                bitcoinSeconds += seconds;
                while (bitcoinSeconds >= 60f)
                {
                    bitcoinSeconds -= 60f;
                    pendingDollars += 2;
                }
            }

            if (pendingDollars > 0 && state.Carries(Bitcoin))
                GrantDollars();
        }

        private void Drain(float amount) => AishiAnomalyS2.Drain(state.Health, amount);
        private void BindHealth()
        {
            for (var type = state.Health.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<, >) || field.FieldType.GetGenericArguments()[0] != typeof(EBodyPart))
                        continue;
                    var map = field.GetValue(state.Health) as IDictionary;
                    if (map == null || !map.Contains(EBodyPart.Head))
                        continue;
                    var first = map[EBodyPart.Head];
                    var healthField = AccessTools.Field(first.GetType(), "Health");
                    var healthProperty = AccessTools.Property(first.GetType(), "Health");
                    if (healthField?.FieldType != typeof(HealthValue) && healthProperty?.PropertyType != typeof(HealthValue))
                        continue;
                    foreach (var part in AishiAnomalyInventory.BodyParts)
                    {
                        var entry = map[part];
                        var value = (healthField != null ? healthField.GetValue(entry) : healthProperty.GetValue(entry, null)) as HealthValue;
                        if (value != null)
                            healthValues[part] = value;
                    }

                    boundHealth = healthValues.Count == 7;
                    return;
                }
            }
        }

        private void GrantDollars()
        {
            if (AishiAnomalyCurrencyNetwork.TryGrant(state.Owner, out int granted))
            {
                pendingDollars = Math.Max(0, pendingDollars - granted);
                return;
            }

            var controller = state.Owner.InventoryController;
            if (controller?.Inventory?.Equipment == null)
                return;
            foreach (var item in state.CarriedItems)
            {
                if (item.TemplateId != Dollars || item.StackObjectsCount >= item.StackMaxSize)
                    continue;
                int amount = Math.Min(pendingDollars, item.StackMaxSize - item.StackObjectsCount);
                item.StackObjectsCount += amount;
                pendingDollars -= amount;
                item.RaiseRefreshEvent(false, true);
                if (pendingDollars == 0)
                    return;
            }

            var currency = Singleton<ItemFactoryNative>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), Dollars, null);
            currency.StackObjectsCount = Math.Min(currency.StackMaxSize, pendingDollars);
            currency.SpawnedInSession = true;
            var address = GridSearch.GetPrioritizedGridsForLoot(controller.Inventory.Equipment, currency).Select(grid => grid.FindLocationForItem(currency)).FirstOrDefault(place => place != null && !ItemActions.Add(currency, place, controller, true).Failed);
            if (address == null)
                return;
            var added = ItemActions.Add(currency, address, controller, false);
            if (added.Failed)
                return;
            added.Value.RaiseEvents(controller, CommandStatus.Succeed);
            pendingDollars -= currency.StackObjectsCount;
        }

        private void ResetHealthBonuses()
        {
            foreach (var pair in bonuses)
                if (healthValues.TryGetValue(pair.Key, out var value))
                {
                    value.Value.Maximum = Mathf.Max(1f, value.Maximum - pair.Value);
                    value.Current = Mathf.Min(value.Current, value.Maximum);
                }

            bonuses.Clear();
        }

        internal void Dispose()
        {
            vision.Dispose();
            ResetHealthBonuses();
            foreach (var effect in effects)
                AccessTools.Method(effect.GetType(), "ForceResidue")?.Invoke(effect, null);
            effects.Clear();
            bonuses.Clear();
            healthValues.Clear();
        }
    }

    [HarmonyPatch(typeof(MovementContext), "get_CanJump")]
    internal static class AishiAnomalyBlockJump
    {
        private static void Postfix(MovementContext __instance, ref bool __result)
        {
            var state = AishiAnomalyInventory.Local;
            if (state != null && state.IsActive && ReferenceEquals(state.Owner.MovementContext, __instance) && state.Extended.BlocksJump)
                __result = false;
        }
    }

    [HarmonyPatch]
    internal static class AishiAnomalyMoveSpeed
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(MovementContext), "MaxSpeed");
            yield return AccessTools.PropertyGetter(typeof(MovementContext), "SprintingSpeed");
        }

        private static void Postfix(MovementContext __instance, ref float __result)
        {
            var state = AishiAnomalyInventory.Local;
            if (state != null && state.IsActive && ReferenceEquals(state.Owner.MovementContext, __instance))
                __result *= state.Extended.SpeedFactor;
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage")]
    internal static class AishiAnomalyProtection
    {
        private static void Prefix(ActiveHealthController __instance, ref float __1, ref DamageInfoStruct __2)
        {
            var state = AishiAnomalyInventory.Local;
            if (__1 > 0 && !__2.OverDamageFrom.HasValue && state != null && state.IsActive && !state.aishiS2.Invulnerable && ReferenceEquals(state.Health, __instance))
            {
                float before = __1;
                if (state.Extended.ProtectsDamage)
                    __1 *= .5f;
                if (__instance.DamageCoeff > 0f)
                    __1 = AishiAnomalyDuck.Absorb(state, __1);
                __2.Damage *= __1 / before;
            }
        }
    }
}
