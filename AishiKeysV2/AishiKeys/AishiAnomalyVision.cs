using System;
using System.Collections.Generic;
using System.Reflection;
using BSG.CameraEffects;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using NightVision = BSG.CameraEffects.NightVision;
#if SPT41
using CameraNative = EFT.CameraControl.CameraManager;
using ItemFactoryNative = EFT.ItemFactory;
using NightObserver = EFT.Player.SlotObserver<EFT.InventoryLogic.NightVisionComponent>;
using NightState = StateSwitcher;

#else
using CameraNative = CameraClass;
using ItemFactoryNative = ItemFactoryClass;
using NightObserver = EFT.Player.GClass2059<EFT.InventoryLogic.NightVisionComponent>;
using NightState = Class687;
#endif
namespace AishiKeys
{
    internal sealed class AishiAnomalyVision
    {
        private NightVision night;
        private ThermalVision thermal;
        private readonly Player owner;
        private NightVisionComponent virtualNight;
        private PlayerCameraController nightController;
        private bool nativeNightApplied, loggedNightFailure;
        internal static int NativeNightDepth;
        private NightSettings nightSettings;
        private Texture thermalMask;
        private float thermalMaskSize;
        private bool originalNightEnabled, originalThermalEnabled;
        private MaskSettings firstMask, secondMask;
        private bool controlled, originalNight, originalThermal, applying, desiredNight, desiredThermal;
        internal static AishiAnomalyVision Active;
#if SPT41
        private static readonly FieldInfo SwitcherField = AccessTools.Field(typeof(NightVision), "_switcher");
        private static readonly FieldInfo PendingField = AccessTools.Field(typeof(NightVision), "_targetState");
#else
        private static readonly FieldInfo SwitcherField = AccessTools.Field(typeof(NightVision), "class687_0");
        private static readonly FieldInfo PendingField = AccessTools.Field(typeof(NightVision), "nullable_0");
#endif
        internal AishiAnomalyVision(Player owner = null)
        {
            this.owner = owner;
        }

        private static void RestoreNightSwitch(NightVision effect, bool value)
        {
            var switcher = (NightState)SwitcherField.GetValue(effect);
            switcher.ForceSwitch(value);
            switcher.Value = 0f;
            PendingField.SetValue(effect, null);
#if SPT41
            effect.Switch(value);
#else
            effect.method_1(value);
#endif
            effect.Update();
        }

        private sealed class NightSettings
        {
            internal Color Color;
            internal float Intensity, NoiseIntensity, NoiseScale, MaskSize;
            internal Texture Mask;
            internal NightSettings(NightVision effect)
            {
                Color = effect.Color;
                Intensity = effect.Intensity;
                NoiseIntensity = effect.NoiseIntensity;
                NoiseScale = effect.NoiseScale;
                Mask = effect.Mask;
                MaskSize = effect.MaskSize;
            }

            internal void Restore(NightVision effect)
            {
                effect.Color = Color;
                effect.Intensity = Intensity;
                effect.NoiseIntensity = NoiseIntensity;
                effect.NoiseScale = NoiseScale;
                effect.Mask = Mask;
                effect.MaskSize = MaskSize;
            }
        }

        private sealed class MaskSettings
        {
            internal readonly TextureMask Effect;
            internal Texture Mask;
            internal float Size;
            internal bool Stretch;
            internal MaskSettings(TextureMask effect)
            {
                Effect = effect;
                Mask = effect.Mask;
                Size = effect.Size;
                Stretch = effect.Stretch;
            }

            internal void Restore()
            {
                if (Effect == null)
                    return;
                Effect.Mask = Mask;
                Effect.Size = Size;
                Effect.Stretch = Stretch;
                Effect.ApplySettings();
            }
        }

        internal static bool BeginNativeNight(object source)
        {
            var current = Active;
            if (current == null || !current.controlled || !current.desiredNight || current.virtualNight == null)
                return false;
            var controller = source as PlayerCameraController;
            if (!ReferenceEquals(source, current.night) && (controller == null || !ReferenceEquals(controller.Player, current.owner)))
                return false;
            NativeNightDepth++;
            return true;
        }

        internal static void SupplyNativeNight(NightObserver observer, ref NightVisionComponent component)
        {
            var current = Active;
            if (NativeNightDepth == 0 || current == null || !current.controlled || !current.desiredNight || current.owner == null || !ReferenceEquals(observer, current.owner.NightVisionObserver))
                return;
            current.originalNight = component?.Togglable?.On ?? false;
            component = current.virtualNight;
        }

        private bool ApplyNativeNight()
        {
            var controller = Singleton<PlayerCameraController>.Instance;
            if (owner == null || owner.NightVisionObserver == null || controller == null || !ReferenceEquals(controller.Player, owner))
                return false;
            if (virtualNight == null)
            {
                var factory = Singleton<ItemFactoryNative>.Instance;
                if (factory == null)
                    return false;
                try
                {
                    var item = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "5c0558060db834001b735271", null);
                    virtualNight = item.GetItemComponent<NightVisionComponent>();
                    if (virtualNight?.Togglable == null)
                        throw new InvalidOperationException("Native GPNVG component unavailable");
                    virtualNight.Togglable.On = true;
                }
                catch (Exception ex)
                {
                    if (!loggedNightFailure)
                    {
                        loggedNightFailure = true;
                        AishiKeysPro.AishiKeysMod.Logger?.LogError("Aishi anomaly NVG activation failed: " + ex);
                    }

                    virtualNight = null;
                    return false;
                }
            }

            if (!nativeNightApplied || nightController != controller || !night.On && !night.InProcessSwitching)
            {
                firstMask?.Restore();
                secondMask?.Restore();
                night.enabled = true;
                controller.method_4();
                nightController = controller;
                nativeNightApplied = true;
            }

            return true;
        }

        internal static void ThermalSettingsRequested(ThermalVision effect)
        {
            var current = Active;
            if (current == null || !current.controlled || !current.desiredThermal || !ReferenceEquals(effect, current.thermal))
                return;
            var mask = effect.ThermalVisionUtilities?.MaskDescription;
            if (mask == null)
                return;
            if (!current.applying)
            {
                if (mask.Mask != Texture2D.blackTexture)
                    current.thermalMask = mask.Mask;
                if (mask.MaskSize != 1f)
                    current.thermalMaskSize = mask.MaskSize;
            }

            mask.Mask = Texture2D.blackTexture;
            mask.MaskSize = 1f;
        }

        internal static void MaskSettingsRequested(TextureMask effect)
        {
            var current = Active;
            if (current == null || !current.controlled || !current.desiredThermal)
                return;
            var saved = current.firstMask?.Effect == effect ? current.firstMask : current.secondMask?.Effect == effect ? current.secondMask : null;
            if (saved == null)
                return;
            if (!current.applying)
            {
                if (effect.Mask != Texture2D.blackTexture)
                    saved.Mask = effect.Mask;
                if (effect.Size != 1f)
                    saved.Size = effect.Size;
                if (!effect.Stretch)
                    saved.Stretch = false;
            }

            effect.Mask = Texture2D.blackTexture;
            effect.Size = 1f;
            effect.Stretch = true;
        }

        internal static void Requested(object effect, ref bool value)
        {
            var current = Active;
            if (current == null || !current.controlled || current.applying)
                return;
            if (ReferenceEquals(effect, current.night))
            {
                if (NativeNightDepth == 0)
                    current.originalNight = value;
                value = current.desiredNight;
            }

            if (ReferenceEquals(effect, current.thermal))
            {
                current.originalThermal = value;
                value = current.desiredThermal;
            }
        }

        internal void Update(bool wantThermal, bool wantNight)
        {
            if (!wantThermal && !wantNight && !controlled)
                return;
            var camera = CameraNative.Instance;
            if (camera == null)
                return;
            if (!wantThermal && !wantNight)
            {
                Dispose();
                return;
            }

            if (night != camera.NightVision || thermal != camera.ThermalVision)
            {
                Dispose();
                night = camera.NightVision;
                thermal = camera.ThermalVision;
            }

            if (!controlled)
            {
                originalNight = night != null && night.On;
                originalThermal = thermal != null && thermal.On;
                if (night != null)
                {
                    nightSettings = new NightSettings(night);
                    originalNightEnabled = night.enabled;
                }

                if (thermal != null)
                {
                    originalThermalEnabled = thermal.enabled;
                    var mask = thermal.ThermalVisionUtilities?.MaskDescription;
                    if (mask != null)
                    {
                        thermalMask = mask.Mask;
                        thermalMaskSize = mask.MaskSize;
                    }
                }

                var first = night != null ? night.TextureMask : null;
                var second = thermal != null ? thermal.TextureMask : null;
                firstMask = first != null ? new MaskSettings(first) : null;
                secondMask = second != null && second != first ? new MaskSettings(second) : null;
                controlled = true;
            }

            Active = this;
            desiredNight = wantNight && !wantThermal;
            desiredThermal = wantThermal;
            applying = true;
            try
            {
                if (thermal != null && desiredThermal)
                {
                    ThermalSettingsRequested(thermal);
                    if (!thermal.enabled)
                        thermal.enabled = true;
                }

                if (thermal != null && thermal.On != wantThermal)
                {
                    thermal.StartSwitch(wantThermal);
                    thermal.FastForwardSwitch();
                }

                if (night != null)
                {
                    if (desiredNight)
                        ApplyNativeNight();
                    else
                    {
                        nativeNightApplied = false;
                        if (night.On || night.InProcessSwitching)
                            RestoreNightSwitch(night, false);
                    }
                }

                if (desiredThermal)
                {
                    ApplyMask(firstMask);
                    ApplyMask(secondMask);
                }
            }
            finally
            {
                applying = false;
            }
        }

        private static void ApplyMask(MaskSettings saved)
        {
            var effect = saved?.Effect;
            if (effect == null || effect.Mask == Texture2D.blackTexture && effect.Size == 1f && effect.Stretch)
                return;
            effect.Mask = Texture2D.blackTexture;
            effect.Size = 1f;
            effect.Stretch = true;
            effect.ApplySettings();
        }

        internal void Dispose()
        {
            if (controlled)
            {
                controlled = false;
                if (night != null)
                {
                    nightSettings?.Restore(night);
                    night.ApplySettings();
                    RestoreNightSwitch(night, originalNight);
                    night.enabled = originalNightEnabled;
                }

                if (thermal != null)
                {
                    var mask = thermal.ThermalVisionUtilities?.MaskDescription;
                    if (mask != null)
                    {
                        mask.Mask = thermalMask;
                        mask.MaskSize = thermalMaskSize;
                    }

                    thermal.StartSwitch(originalThermal);
                    thermal.FastForwardSwitch();
                    thermal.enabled = originalThermalEnabled;
                }

                firstMask?.Restore();
                secondMask?.Restore();
                var controller = Singleton<PlayerCameraController>.Instance;
                if (night != null && owner != null && owner.NightVisionObserver != null && controller != null && ReferenceEquals(controller.Player, owner))
                {
                    controller.method_4();
                }
                else if (night != null && originalNight)
                    night.ApplySettings();
                if (thermal != null && originalThermal)
                    thermal.SetMaterialProperties();
            }

            controlled = false;
            night = null;
            thermal = null;
            nightSettings = null;
            firstMask = null;
            secondMask = null;
            thermalMask = null;
            nativeNightApplied = false;
            nightController = null;
            virtualNight = null;
            if (ReferenceEquals(Active, this))
                Active = null;
        }
    }

    [HarmonyPatch(typeof(NightObserver), "get_Component")]
    internal static class AishiAnomalyNativeNightComponent
    {
        private static void Postfix(NightObserver __instance, ref NightVisionComponent __result) => AishiAnomalyVision.SupplyNativeNight(__instance, ref __result);
    }

    [HarmonyPatch]
    internal static class AishiAnomalyNativeNightScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerCameraController), "method_4");
            foreach (var name in new[]
            {
                "ApplySettings",
                "SetMask",
                "StartSwitch",
                "OnPreCull"
            }

            )
                yield return AccessTools.Method(typeof(NightVision), name);
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(object __instance, out bool __state) => __state = AishiAnomalyVision.BeginNativeNight(__instance);
        private static void Finalizer(bool __state)
        {
            if (__state)
                AishiAnomalyVision.NativeNightDepth--;
        }
    }

    [HarmonyPatch(typeof(ThermalVision), "SetMaterialProperties")]
    internal static class AishiAnomalyThermalSettings
    {
        private static void Prefix(ThermalVision __instance) => AishiAnomalyVision.ThermalSettingsRequested(__instance);
    }

    [HarmonyPatch(typeof(TextureMask), "ApplySettings")]
    internal static class AishiAnomalyMaskSettings
    {
        private static void Prefix(TextureMask __instance) => AishiAnomalyVision.MaskSettingsRequested(__instance);
    }

    [HarmonyPatch(typeof(NightVision), "StartSwitch")]
    internal static class AishiAnomalyNightSwitch
    {
        private static void Prefix(NightVision __instance, ref bool __0) => AishiAnomalyVision.Requested(__instance, ref __0);
    }

    [HarmonyPatch(typeof(ThermalVision), "StartSwitch")]
    internal static class AishiAnomalyThermalSwitch
    {
        private static void Prefix(ThermalVision __instance, ref bool __0) => AishiAnomalyVision.Requested(__instance, ref __0);
    }
}
