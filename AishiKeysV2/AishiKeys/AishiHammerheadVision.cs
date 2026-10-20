using System;
using System.Threading.Tasks;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using CameraNative = EFT.CameraControl.CameraManager;
using ItemFactoryNative = EFT.ItemFactory;
using NightObserver = EFT.Player.SlotObserver<EFT.InventoryLogic.NightVisionComponent>;
#else
using CameraNative = CameraClass;
using ItemFactoryNative = ItemFactoryClass;
using NightObserver = EFT.Player.GClass2059<EFT.InventoryLogic.NightVisionComponent>;
#endif

namespace AishiKeys
{
    internal sealed class AishiHammerheadVision : MonoBehaviour
    {
        internal enum VisionMode { Off, Night, Thermal }
        internal static AishiHammerheadVision Active;
        internal VisionMode Mode { get; private set; }
        private Player _owner;
        private Item _helmet;
        private ThermalVisionComponent _thermal;
        private Texture _nightMask;
        private float _nightMaskSize;
        private bool _busy, _lastOn;
        private float _nextInput;
        private int _refreshVersion;
        private bool Equipped => _owner?.IsYourPlayer == true && _owner.HealthController?.IsAlive == true &&
            _helmet != null && ReferenceEquals(_owner.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem, _helmet);

        internal static AishiHammerheadVision Ensure(Player owner)
        {
            if (owner?.IsYourPlayer != true || AishiKeysMod.Instance?.NetworkBridge?.IsHeadless == true || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            var helmet = owner.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem;
            if (!AishiHammerhead.IsHelmet(helmet)) return null;
            var vision = owner.GetComponent<AishiHammerheadVision>() ?? owner.gameObject.AddComponent<AishiHammerheadVision>();
            vision._owner = owner;
            Active = vision;
            if (!ReferenceEquals(vision._helmet, helmet))
            {
                vision._helmet = helmet;
                vision._thermal = null;
                vision._nightMask = null;
                vision._lastOn = helmet.GetItemComponent<NightVisionComponent>()?.Togglable?.On == true;
                vision.Mode = vision._lastOn ? VisionMode.Night : VisionMode.Off;
            }
            return vision;
        }

        private ThermalVisionComponent ThermalComponent()
        {
            if (_thermal != null) return _thermal;
            var sample = Singleton<ItemFactoryNative>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "5c110624d174af029e69734c", null).GetItemComponent<ThermalVisionComponent>();
            if (sample == null) throw new InvalidOperationException("Hammerhead native thermal template is unavailable.");
            var template = AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(sample.Template, null);
            var night = _helmet.GetItemComponent<NightVisionComponent>();
            SetTemplateValue(template, "Mask", night.Template.Mask);
            SetTemplateValue(template, "MaskSize", night.Template.MaskSize);
            _thermal = (ThermalVisionComponent)Activator.CreateInstance(typeof(ThermalVisionComponent), _helmet, template, night.Togglable);
            return _thermal;
        }

        private static void SetTemplateValue(object template, string name, object value)
        {
            var field = AccessTools.Field(template.GetType(), name);
            if (field != null) field.SetValue(template, value);
            else
            {
                var setter = AccessTools.PropertySetter(template.GetType(), name);
                if (setter == null) throw new InvalidOperationException("Hammerhead thermal setting is unavailable: " + name);
                setter.Invoke(template, new[] { value });
            }
        }

        internal async Task Cycle()
        {
            if (_busy || Time.unscaledTime < _nextInput || !Equipped || _owner.InventoryController == null) return;
            var toggle = _helmet.GetItemComponent<NightVisionComponent>()?.Togglable;
            if (toggle == null) return;
            if (!AishiHammerheadPower.HasPower(_helmet))
            {
                AishiHammerheadPower.NotifyEmpty();
                return;
            }
            _busy = true;
            var previous = Mode;
            var helmet = _helmet;
            var next = !toggle.On || Mode == VisionMode.Off ? VisionMode.Night : Mode == VisionMode.Night ? VisionMode.Thermal : VisionMode.Off;
            try
            {
                if (next == VisionMode.Thermal)
                {
                    ThermalComponent();
                    var night = CameraNative.Instance?.NightVision;
                    _nightMask = night?.Mask;
                    _nightMaskSize = night != null ? night.MaskSize : 1f;
                }
                Mode = next;
                bool on = next != VisionMode.Off;
                if (toggle.On != on)
                {
                    var result = await _owner.InventoryController.TryRunNetworkTransaction(toggle.Set(on, true, false), null);
                    if (result == null || !result.Succeed)
                    {
                        Mode = previous;
                        return;
                    }
                }
                if (!Equipped || !ReferenceEquals(helmet, _helmet)) return;
                _lastOn = toggle.On;
                _owner.MovementContext.SetInteractInHands(EInteraction.HelmetRailGear);
                await RefreshCamera();
                _nextInput = Time.unscaledTime + .35f;
            }
            catch (Exception error)
            {
                Mode = previous;
                AishiKeysMod.Logger?.LogError("Hammerhead vision switch failed: " + error.GetBaseException().Message);
                await RefreshCamera();
            }
            finally { _busy = false; }
        }

        private async Task RefreshCamera()
        {
            int revision = ++_refreshVersion;
            var controller = Singleton<PlayerCameraController>.Instance;
            var camera = CameraNative.Instance;
            if (controller == null || camera == null || !ReferenceEquals(controller.Player, _owner) || AishiAnomalyVision.Active != null) return;
            if (Mode == VisionMode.Thermal && Equipped)
            {
                camera.NightVision.StartSwitch(false);
                while (camera.NightVision != null && camera.NightVision.enabled && (camera.NightVision.On || camera.NightVision.InProcessSwitching))
                {
                    await Task.Delay(16);
                    if (this == null || revision != _refreshVersion || camera != CameraNative.Instance) return;
                }
                if (!Equipped || Mode != VisionMode.Thermal) return;
                controller.method_5();
            }
            else
            {
                camera.ThermalVision.StartSwitch(false);
                while (camera.ThermalVision != null && camera.ThermalVision.enabled && (camera.ThermalVision.On || camera.ThermalVision.InProcessSwitching))
                {
                    await Task.Delay(16);
                    if (this == null || revision != _refreshVersion || camera != CameraNative.Instance) return;
                }
                controller.method_4();
            }
            await Task.Delay(16);
            while (this != null && revision == _refreshVersion && camera == CameraNative.Instance &&
                (camera.NightVision.InProcessSwitching || camera.ThermalVision.InProcessSwitching))
                await Task.Delay(16);
        }

        private void LateUpdate()
        {
            if (_busy) return;
            if (!Equipped)
            {
                if (_helmet != null)
                {
                    _helmet = null;
                    _thermal = null;
                    _nightMask = null;
                    Mode = VisionMode.Off;
                    _ = RefreshCamera();
                }
                return;
            }
            bool on = _helmet.GetItemComponent<NightVisionComponent>()?.Togglable?.On == true;
            if (!AishiHammerheadPower.HasPower(_helmet)) on = false;
            if (on == _lastOn) return;
            _lastOn = on;
            Mode = on ? VisionMode.Night : VisionMode.Off;
            _ = RefreshCamera();
        }

        internal static void SupplyNight(NightObserver observer, ref NightVisionComponent component)
        {
            var vision = Active;
            if (vision == null || !vision.Equipped || !ReferenceEquals(observer, vision._owner.NightVisionObserver) || AishiAnomalyVision.Active != null) return;
            if (vision.Mode != VisionMode.Night || !AishiHammerheadPower.HasPower(vision._helmet)) component = null;
        }

        internal static bool ApplyThermal(PlayerCameraController controller)
        {
            var vision = Active;
            if (vision == null || !vision.Equipped || vision.Mode != VisionMode.Thermal ||
                !ReferenceEquals(controller.Player, vision._owner) || AishiAnomalyVision.Active != null) return false;
            var camera = CameraNative.Instance;
            var thermal = camera?.ThermalVision;
            if (thermal == null || !thermal.enabled) return true;
            if (!AishiHammerheadPower.HasPower(vision._helmet))
            {
                thermal.StartSwitch(false);
                return true;
            }
            if (camera.NightVision.On || camera.NightVision.InProcessSwitching) return true;
            var template = vision.ThermalComponent().Template;
            var utilities = thermal.ThermalVisionUtilities;
            utilities.CurrentRampPalette = template.RampPalette;
            utilities.DepthFade = template.DepthFade;
            utilities.ValuesCoefs.MainTexColorCoef = template.MainTexColorCoef;
            utilities.ValuesCoefs.MinimumTemperatureValue = template.MinimumTemperatureValue;
            utilities.ValuesCoefs.RampShift = template.RampShift;
            thermal.IsNoisy = template.IsNoisy;
            utilities.NoiseParameters.NoiseIntensity = template.NoiseInstensity;
            thermal.IsFpsStuck = template.IsFpsStuck;
            thermal.IsGlitch = template.IsGlitch;
            thermal.IsMotionBlurred = template.IsMotionBlurred;
            thermal.SetMask(template.Mask);
            utilities.MaskDescription.MaskSize = template.MaskSize;
            thermal.IsPixelated = template.IsPixelated;
            thermal.PixelationUtilities.BlockCount = template.PixelationBlockCount;
            thermal.SetMaterialProperties();
            thermal.StartSwitch(true);
            return true;
        }

        internal static void ApplyThermalMask(ThermalVision thermal)
        {
            var vision = Active;
            if (vision == null || !vision.Equipped || vision.Mode != VisionMode.Thermal || vision._nightMask == null ||
                !ReferenceEquals(thermal, CameraNative.Instance?.ThermalVision) || AishiAnomalyVision.Active != null) return;
            var mask = thermal.ThermalVisionUtilities?.MaskDescription;
            if (mask == null) return;
            mask.Mask = vision._nightMask;
            mask.MaskSize = vision._nightMaskSize;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            _helmet = null;
            _thermal = null;
            _nightMask = null;
            Mode = VisionMode.Off;
        }
    }

    [HarmonyPatch(typeof(NightObserver), "get_Component")]
    internal static class AishiHammerheadNightComponent
    {
        private static void Postfix(NightObserver __instance, ref NightVisionComponent __result) => AishiHammerheadVision.SupplyNight(__instance, ref __result);
    }

    [HarmonyPatch(typeof(PlayerCameraController), "method_5")]
    internal static class AishiHammerheadThermalCamera
    {
        private static bool Prefix(PlayerCameraController __instance) => !AishiHammerheadVision.ApplyThermal(__instance);
    }

    [HarmonyPatch(typeof(ThermalVision), "SetMaterialProperties")]
    internal static class AishiHammerheadThermalMask
    {
        private static void Prefix(ThermalVision __instance) => AishiHammerheadVision.ApplyThermalMask(__instance);
    }
}
