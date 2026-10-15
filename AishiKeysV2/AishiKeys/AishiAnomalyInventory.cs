using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using DamageInfoStruct = EFT.Ballistics.DamageInfo;

#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
    internal static class AishiAnomalyRaidStart
    {
        private static float nextCheck;
        internal static void EnsureRuntime()
        {
            if (Time.realtimeSinceStartup < nextCheck)
                return;
            nextCheck = Time.realtimeSinceStartup + .5f;
            if (!Singleton<GameWorld>.Instantiated || AishiKeysPro.AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true)
                return;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || world is HideoutGameWorld)
                return;
            var player = world.MainPlayer;
            if (player == null || !player.IsYourPlayer || !(player.HealthController is ActiveHealthController health) || !health.IsAlive || player.InventoryController?.Inventory?.Equipment == null)
                return;
            var state = player.GetComponent<AishiAnomalyInventory>() ?? player.gameObject.AddComponent<AishiAnomalyInventory>();
            state.Initialize(player, world);
        }

        private static void Postfix(GameWorld __instance)
        {
            if (__instance is HideoutGameWorld || AishiKeysPro.AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true)
                return;
            var bootstrap = __instance.GetComponent<AishiAnomalyBootstrap>() ?? __instance.gameObject.AddComponent<AishiAnomalyBootstrap>();
            bootstrap.World = __instance;
        }
    }

    internal sealed class AishiAnomalyBootstrap : MonoBehaviour
    {
        internal GameWorld World;
        private float nextAttempt;
        private void Update()
        {
            if (World == null || AishiKeysPro.AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true)
            {
                Destroy(this);
                return;
            }

            if (Time.time < nextAttempt)
                return;
            nextAttempt = Time.time + .25f;
            var player = World.MainPlayer;
            if (player == null || !player.IsYourPlayer || !(player.HealthController is ActiveHealthController) || player.InventoryController?.Inventory?.Equipment == null)
                return;
            var state = player.GetComponent<AishiAnomalyInventory>() ?? player.gameObject.AddComponent<AishiAnomalyInventory>();
            state.Initialize(player, World);
            Destroy(this);
        }
    }

    internal sealed class AishiAnomalyInventory : MonoBehaviour
    {
        internal const string Cat = "6183f2a51cb013b5ab62a796", Weight = "fac4f1a658ad562cddde72c6", Kunai = "72eb2bb2416ee48ee80d63d4", Sneaker = "a38cbf3abbfdcbedaa1b9a0e", Parrot = "b543125009b989d7287b58ab", Bear = "9644da05612ad219e7fc4387", Mask = "cd19a7a32ec3820a6ea48800", Raven = "1254eb68d298789e43b45ed3";
        internal static AishiAnomalyInventory Local { get; private set; }
        internal Player Owner { get; private set; }
        internal ActiveHealthController Health { get; private set; }
        internal bool HasKunai => Carries(Kunai);
        internal bool HasSneaker => Carries(Sneaker);
        internal bool HasRaven => Carries(Raven);

        private bool ravenWeightApplied;
        private GameWorld world;
        private readonly HashSet<Item> visited = new HashSet<Item>();
        private readonly List<Item> activeAnomalies = new List<Item>();
        private readonly HashSet<string> failedSections = new HashSet<string>();
        internal static readonly EBodyPart[] BodyParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg
        };
        private float nextScan, lastTick, blinkAt, blackUntil;
        private Action _updateaishiS2, _updateExtended;
        private bool wearingMask, activated;
        internal AishiAnomalyExtended Extended { get; private set; }
        internal AishiAnomalyS2 aishiS2 { get; private set; }

        internal bool Carries(string id)
        {
            foreach (var item in activeAnomalies)
                if (item.TemplateId == id && !AishiAnomalyContainment.IsSuppressed(item))
                    return true;
            return false;
        }

        internal IEnumerable<Item> CarriedItems => visited.Where(item => !AishiAnomalyContainment.IsSuppressed(item));

        private Canvas blinkCanvas;
        internal bool IsActive => Owner != null && world != null && Health != null && Health.IsAlive && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, world);

        internal void Initialize(Player player, GameWorld gameWorld)
        {
            if (ReferenceEquals(Owner, player) && ReferenceEquals(Health, player.HealthController) && ReferenceEquals(world, gameWorld) && Extended != null)
                return;
            Owner = player;
            Health = (ActiveHealthController)player.HealthController;
            world = gameWorld;
            Local = this;
            aishiS2 = new AishiAnomalyS2(this);
            Extended = new AishiAnomalyExtended(this);
            _updateaishiS2 = aishiS2.UpdatePresentation;
            _updateExtended = Extended.UpdatePresentation;
            lastTick = Time.realtimeSinceStartup;
            nextScan = 0;
        }

        private void Scan()
        {
            activeAnomalies.Clear();
            visited.Clear();
            var equipment = Owner.InventoryController?.Inventory?.Equipment ?? Owner.Inventory?.Equipment ?? Owner.Profile?.Inventory?.Equipment;
            if (equipment != null)
                foreach (var item in equipment.GetAllItems())
                {
                    if (item == null || !visited.Add(item))
                        continue;
                    if (AishiInventoryDescriptions.IsAnomaly(item.TemplateId) && !AishiAnomalyContainment.IsSuppressed(item))
                        activeAnomalies.Add(item);
                }

            bool raven = Carries(Raven);
            if (raven != ravenWeightApplied)
            {
                ravenWeightApplied = raven;
                Owner.Physical.OnWeightLimitsUpdated();
            }

            bool mask = equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem?.TemplateId == Mask;
            if (mask && !wearingMask)
                blinkAt = Time.realtimeSinceStartup + 10f;
            if (!mask)
            {
                blackUntil = 0;
                if (blinkCanvas != null)
                    blinkCanvas.enabled = false;
            }

            wearingMask = mask;
        }

        private void Update()
        {
            if (AishiKeysPro.AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true || Owner != null && !Owner.IsYourPlayer)
            {
                Destroy(this);
                return;
            }

            if (!IsActive)
            {
                if (activated || Owner == null || world == null)
                    Destroy(this);
                return;
            }

            if (!activated)
            {
                activated = true;
                lastTick = Time.realtimeSinceStartup;
                Scan();
                Owner.Physical.OnWeightLimitsUpdated();
            }

            float now = Time.realtimeSinceStartup;
            if (now >= nextScan)
            {
                Scan();
                nextScan = now + .5f;
            }

            RunSection("expanded effects", _updateaishiS2);
            RunSection("inventory presentation", _updateExtended);
            if (now - lastTick >= 1f)
            {
                float seconds = now - lastTick;
                lastTick = now;
                RunSection("expanded timers", () => aishiS2.Tick(seconds));
                RunSection("inventory timers", () => Extended.Tick(seconds));
                float flatDamage = (Carries(Cat) ? 2f : 0f) + (HasRaven ? 2f : 0f);
                if (flatDamage > 0f)
                    DamageOneLimb(Health, flatDamage * seconds);
                if (Carries(Parrot))
                    DamageEachLimb(Health, 3f * seconds);
                if (Carries(Bear))
                    AishiAnomalyS2.Drain(Health, seconds);
                float stamina = (Carries(Weight) ? 50f : 0f) + (HasRaven ? 5f : 0f);
                if (stamina > 0 && Health.IsAlive)
                {
                    var pool = Owner.Physical.Stamina;
                    pool.UpdateStamina(Mathf.Min(pool.TotalCapacity, pool.Current + stamina * seconds));
                }
            }

            if (wearingMask && now >= blinkAt)
            {
                blackUntil = now + .5f;
                blinkAt = now + 10f;
            }

            bool blink = wearingMask && now < blackUntil && Health.IsAlive;
            if (blink && blinkCanvas == null)
                CreateBlink();
            if (blinkCanvas != null)
                blinkCanvas.enabled = blink;
        }

        internal void RunSection(string name, Action action)
        {
            try
            {
                action();
                failedSections.Remove(name);
            }
            catch (Exception error)
            {
                if (failedSections.Add(name))
                    AishiKeysPro.AishiKeysMod.Logger.LogError("Aishi KTE " + name + ": " + error);
            }
        }

        internal static void DamageOneLimb(ActiveHealthController health, float amount)
        {
            if (health == null || !health.IsAlive || amount <= 0f)
                return;
            int start = UnityEngine.Random.Range(0, BodyParts.Length);
            for (int i = 0; i < BodyParts.Length; i++)
            {
                var part = BodyParts[(start + i) % BodyParts.Length];
                if (health.IsBodyPartDestroyed(part) || health.GetBodyPartHealth(part, false).Current <= 0f)
                    continue;
                health.ApplyDamage(part, amount, new DamageInfoStruct { DamageType = EDamageType.Poison, Damage = amount });
                return;
            }
        }

        internal static void DamageEachLimb(ActiveHealthController health, float amount)
        {
            if (health == null || !health.IsAlive || amount <= 0)
                return;
            var damage = new DamageInfoStruct
            {
                DamageType = EDamageType.Poison,
                Damage = amount
            };
            foreach (var limb in BodyParts)
                if (health.GetBodyPartHealth(limb, false).Current > 0)
                    health.ApplyDamage(limb, amount, damage);
        }

        private void CreateBlink()
        {
            var root = new GameObject("Aishi anomalous blink", typeof(Canvas));
            root.transform.SetParent(transform, false);
            blinkCanvas = root.GetComponent<Canvas>();
            blinkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            blinkCanvas.sortingOrder = 32760;
            var panel = new GameObject("Black", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(root.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = panel.GetComponent<UnityEngine.UI.Image>();
            image.color = Color.black;
            image.raycastTarget = false;
        }

        private void OnDestroy()
        {
            bool refreshWeight = IsActive;
            if (ReferenceEquals(Local, this))
                Local = null;
            RunSection("expanded cleanup", () => aishiS2?.Dispose());
            RunSection("inventory cleanup", () => Extended?.Dispose());
            if (blinkCanvas != null)
                Destroy(blinkCanvas.gameObject);
            activeAnomalies.Clear();
            ravenWeightApplied = false;
            if (refreshWeight && Owner.Physical != null)
                Owner.Physical.OnWeightLimitsUpdated();
        }
    }

    [HarmonyPatch]
    internal static class AishiRavenWeight
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(ActiveHealthController), "CarryingWeightRelativeModifier");
        private static void Postfix(object __instance, ref float __result)
        {
            var state = AishiAnomalyInventory.Local;
            if (state != null && state.IsActive && state.HasRaven && ReferenceEquals(state.Health, __instance))
                __result *= 2f;
        }
    }
}
