using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using ItemFactoryNative = EFT.ItemFactory;
#else
using ItemFactoryNative = ItemFactoryClass;
#endif

namespace AishiKeys
{
    internal static class AishiHammerhead
    {
        internal const string TemplateId = "7f3d1490eb7e5442947f28f5";
        private static object _nightTemplate, _lightTemplate;
        internal static bool IsHelmet(Item item) => item != null && item.TemplateId.ToString() == TemplateId;
        internal static void AttachComponents(Item item)
        {
            if (!IsHelmet(item) || item.GetItemComponent<NightVisionComponent>() != null) return;
            var factory = Singleton<ItemFactoryNative>.Instance;
            if (_nightTemplate == null)
            {
                var sample = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "5c0558060db834001b735271", null).GetItemComponent<NightVisionComponent>();
                _nightTemplate = sample.Template;
            }
            if (_lightTemplate == null)
            {
                var sample = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), "56def37dd2720bec348b456a", null).GetItemComponent<LightComponent>();
                if (sample == null) throw new InvalidOperationException("Hammerhead native flashlight template is unavailable.");
                _lightTemplate = sample.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).First(f => f.FieldType.IsInterface && f.FieldType.GetProperty("ModesCount") != null).GetValue(sample);
                _lightTemplate = AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(_lightTemplate, null);
                var modeField = AccessTools.Field(_lightTemplate.GetType(), "ModesCount");
                if (modeField != null) modeField.SetValue(_lightTemplate, 1);
                else AccessTools.PropertySetter(_lightTemplate.GetType(), "ModesCount").Invoke(_lightTemplate, new object[] { 1 });
            }
            var toggle = new TogglableComponent(item);
            item.Components.Add(toggle);
            item.Components.Add((IItemComponent)Activator.CreateInstance(typeof(NightVisionComponent), item, _nightTemplate, toggle));
            item.Components.Add((IItemComponent)Activator.CreateInstance(typeof(LightComponent), item, _lightTemplate));
        }

        internal static void BindVisual(PlayerBody body, GameObject model)
        {
            var player = body.GetComponentInParent<Player>();
            var item = player?.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem;
            if (!IsHelmet(item) || model == null) return;
            var view = body.GetSlotViewByItem(item);
            if (view == null || view.Model != model) return;
            AttachComponents(item);
            var visual = model.GetComponent<HammerheadVisual>() ?? model.AddComponent<HammerheadVisual>();
            visual.Initialize(player, item);
        }

        internal static void PrepareLamp(TacticalComboVisualController controller)
        {
            if (!controller.name.StartsWith("Aishi_hammerhead", StringComparison.Ordinal) && !IsHelmet(controller.LightMod?.Item)) return;
            if (controller.transform.GetComponentsInChildren<Transform>(true).Any(t => t.name == "mode_000")) return;
            var body = controller.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "THEMASK1") ?? controller.transform;
            var off = new GameObject("mode_000"); off.transform.SetParent(body, false);
            var on = new GameObject("mode_001"); on.transform.SetParent(body, false);
            on.transform.localPosition = new Vector3(0f, -.075f, .103f);
            on.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var lamp = on.AddComponent<Light>();
                lamp.type = LightType.Spot; lamp.range = 35f; lamp.spotAngle = 55f; lamp.intensity = 2.5f;
                lamp.color = new Color(1f, .97f, .88f); lamp.shadows = LightShadows.None;
            }
        }
    }

    internal sealed class HammerheadVisual : MonoBehaviour
    {
        private NightVisionComponent _night;
        private LightComponent _light;
        private Action _unsubscribe;
        private Light _visorGlow;
        private Renderer _renderer;
        private Material[] _materials;
        private readonly Dictionary<Renderer, Material[]> _surfaces = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Renderer, MaterialPropertyBlock[]> _originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock[]>();
        private MaterialPropertyBlock _block;
        private bool _lastLight;
        private GameObject _lampRoot;
        private Transform _beam;
        private Player _player;
        private Item _item;
        private TacticalComboVisualController _controller;
        private readonly List<Material> _runtimeMaterials = new List<Material>();
        private readonly List<int> _lenses = new List<int>();
        private readonly List<int> _bulbs = new List<int>();
        private static readonly FieldInfo Controllers = AccessTools.Field(typeof(Player), "_helmetLightControllers");

        private object _nightObserver;
        private Slot _nightOriginalSlot;
        private FieldInfo _nightSlotField;
        private static readonly PropertyInfo NightObserver = AccessTools.Property(typeof(Player), "NightVisionObserver");

        private void BindNightObserver()
        {
            if (_nightObserver != null || _player == null || !ReferenceEquals(_player.Equipment.GetSlot(EquipmentSlot.FaceCover).ContainedItem, _item)) return;
            var observer = NightObserver?.GetValue(_player, null);
            if (observer == null) return;
            _nightSlotField = observer.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(Slot));
            _nightOriginalSlot = (Slot)_nightSlotField.GetValue(observer);
            _nightObserver = observer;
            _nightSlotField.SetValue(observer, _player.Equipment.GetSlot(EquipmentSlot.FaceCover));
            AccessTools.Method(observer.GetType(), "Update").Invoke(observer, null);
        }

        internal void Initialize(Player player, Item item)
        {
            if (ReferenceEquals(_player, player) && ReferenceEquals(_item, item)) return;
            Release();
            _player = player;
            _item = item;
            _night = item.GetItemComponent<NightVisionComponent>();
            _light = item.GetItemComponent<LightComponent>();
            if (_night?.Togglable == null || _light == null || Controllers == null) throw new InvalidOperationException("Hammerhead native equipment components are unavailable.");
            var controller = GetComponent<TacticalComboVisualController>();
            if (controller == null) controller = gameObject.AddComponent<TacticalComboVisualController>();
            controller.LightMod = _light;
            _controller = controller;
            AishiHammerhead.PrepareLamp(controller);
            _beam = controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "mode_001");
            controller.Init(); controller.UpdateBeams(player.IsYourPlayer);
            bool rendering = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var existing = Controllers.GetValue(player) as IEnumerable<TacticalComboVisualController>;
            Controllers.SetValue(player, (existing ?? Enumerable.Empty<TacticalComboVisualController>()).Where(c => c != null && c != controller).Concat(new[] { controller }).ToArray());
            if (player.IsYourPlayer) player.SendHeadlightsPacket(true);
            BindNightObserver();
            if (!rendering) return;
            _renderer = GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("hammerhead_", StringComparison.Ordinal)));
            if (_renderer != null)
            {
                _materials = _renderer.sharedMaterials;
                _block = new MaterialPropertyBlock();
                for (int i = 0; i < _materials.Length; i++)
                {
                    string name = _materials[i]?.name ?? "";
                    if (name.Contains("Face Glass") || name.Contains("Camera Diodes") || name.Contains("NVG Glass")) _lenses.Add(i);
                    if (name.Contains("Headlight Insides") || name == "hammerhead_Diodes") _bulbs.Add(i);
                }
                var display = (Material[])_materials.Clone();
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("Hammerhead indicator shader is unavailable.");
                foreach (int slot in _lenses.Concat(_bulbs).Distinct())
                {
                    if (_materials[slot].name.Contains("Face Glass")) continue;
                    bool bulb = _bulbs.Contains(slot);
                    var indicator = bulb ? new Material(_materials[slot]) : new Material(shader) { color = Color.black };
                    indicator.name = _materials[slot].name + " indicator";
                    if (bulb) indicator.EnableKeyword("_EMISSION");
                    _runtimeMaterials.Add(indicator);
                    display[slot] = indicator;
                }
                foreach (var surface in GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("hammerhead_", StringComparison.Ordinal))))
                {
                    _surfaces[surface] = surface.sharedMaterials;
                    var originalBlocks = new MaterialPropertyBlock[display.Length];
                    for (int slot = 0; slot < originalBlocks.Length; slot++)
                    {
                        originalBlocks[slot] = new MaterialPropertyBlock();
                        surface.GetPropertyBlock(originalBlocks[slot], slot);
                    }
                    _originalBlocks[surface] = originalBlocks;
                    surface.sharedMaterials = display;
                }
                var glow = new GameObject("Hammerhead visor glow"); glow.transform.SetParent(transform, false); _lampRoot = glow;
                _visorGlow = glow.AddComponent<Light>(); _visorGlow.type = LightType.Point; _visorGlow.range = .3f;
                _visorGlow.color = new Color(.1f, 1f, .25f); _visorGlow.intensity = .04f; _visorGlow.shadows = LightShadows.None;
                _unsubscribe = _night.Togglable.OnChanged.Subscribe(ApplyNight);
                ApplyNight(); ApplyLamp();
            }
        }

        private void ApplyNight()
        {
            bool on = _night?.Togglable?.On == true;
            if (_visorGlow != null) _visorGlow.enabled = on;
            if (_renderer == null) return;
            foreach (int slot in _lenses)
            {
                foreach (var surface in _surfaces.Keys)
                {
                if (surface == null) continue;
                surface.GetPropertyBlock(_block, slot);
                bool visor = _materials[slot].name.Contains("Face Glass");
                _block.SetColor("_Color", on ? (visor ? new Color(.025f, .05f, .028f, .97f) : new Color(.08f, .8f, .16f)) : (visor ? _materials[slot].GetColor("_Color") : Color.black));
                _block.SetColor("_EmissionColor", on ? new Color(.08f, 1f, .2f) * (visor ? .12f : .8f) : Color.black);
                surface.SetPropertyBlock(_block, slot);
                }
            }
        }

        private void ApplyLamp()
        {
            if (_renderer == null) return;
            _lastLight = _light.IsActive;
            foreach (int slot in _bulbs)
            {
                foreach (var surface in _surfaces.Keys)
                {
                if (surface == null) continue;
                surface.GetPropertyBlock(_block, slot);
                _block.SetColor("_Color", _lastLight ? new Color(1f, .97f, .88f) : _materials[slot].GetColor("_Color"));
                _block.SetColor("_EmissionColor", _lastLight ? new Color(1f, .97f, .88f) * 1.5f : Color.black);
                surface.SetPropertyBlock(_block, slot);
                }
            }
        }

        private void TryRebindVisual()
        {
            if (_player != null) return;
            var player = GetComponentInParent<Player>();
            var item = player?.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem;
            if (!AishiHammerhead.IsHelmet(item)) return;
            var body = player.GetComponentInChildren<PlayerBody>(true);
            if (body?.GetSlotViewByItem(item)?.Model == gameObject) Initialize(player, item);
        }

        private void OnEnable() => TryRebindVisual();

        private void LateUpdate()
        {
            TryRebindVisual();
            if (_nightObserver == null) BindNightObserver();
            if (_beam != null && _player != null && _light?.IsActive == true && _player.LookDirection.sqrMagnitude > .001f)
                _beam.rotation = Quaternion.LookRotation(_player.LookDirection, Vector3.up);
            if (_light != null && _light.IsActive != _lastLight) ApplyLamp();
        }
        private void Release()
        {
            _unsubscribe?.Invoke(); _unsubscribe = null;
            if (_nightObserver != null)
            {
                _nightSlotField.SetValue(_nightObserver, _nightOriginalSlot);
                AccessTools.Method(_nightObserver.GetType(), "Update").Invoke(_nightObserver, null);
            }
            _nightObserver = null; _nightOriginalSlot = null; _nightSlotField = null;
            if (_player != null && Controllers != null)
            {
                var existing = Controllers.GetValue(_player) as IEnumerable<TacticalComboVisualController>;
                if (existing != null) Controllers.SetValue(_player, existing.Where(c => c != null && c != _controller).ToArray());
            }
            foreach (var surface in _surfaces)
            {
                if (surface.Key == null) continue;
                surface.Key.sharedMaterials = surface.Value;
                if (_originalBlocks.TryGetValue(surface.Key, out var blocks))
                    for (int slot = 0; slot < blocks.Length; slot++) surface.Key.SetPropertyBlock(blocks[slot], slot);
            }
            _originalBlocks.Clear();
            _surfaces.Clear();
            foreach (var material in _runtimeMaterials) if (material != null) Destroy(material);
            _runtimeMaterials.Clear(); _lenses.Clear(); _bulbs.Clear();
            if (_lampRoot != null) Destroy(_lampRoot);
            if (_controller != null) _controller.LightMod = null;
            _player = null; _item = null; _controller = null; _lampRoot = null; _beam = null;
            _renderer = null; _materials = null; _night = null; _light = null; _visorGlow = null;
        }
        private void OnDisable() => Release();
        private void OnDestroy() => Release();
    }

    [HarmonyPatch(typeof(TacticalComboVisualController), "Awake")]
    internal static class AishiHammerheadLampAwake
    {
        private static void Prefix(TacticalComboVisualController __instance) => AishiHammerhead.PrepareLamp(__instance);
    }

    [HarmonyPatch(typeof(TacticalComboVisualController), "UpdateBeams")]
    internal static class AishiHammerheadLampReady
    {
        private static void Prefix(TacticalComboVisualController __instance)
        {
            if (!AishiHammerhead.IsHelmet(__instance.LightMod?.Item)) return;
            AishiHammerhead.PrepareLamp(__instance);
            __instance.Init();
        }
    }

    [HarmonyPatch]
    internal static class AishiHammerheadComponents
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(ItemFactoryNative).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "CreateItem" && m.ReturnType == typeof(Item));
        private static void Postfix(Item __result) => AishiHammerhead.AttachComponents(__result);
    }

    [HarmonyPatch]
    internal static class AishiHammerheadEquipmentVisual
    {
        private static MethodBase TargetMethod() => typeof(PlayerBody).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)).Single(m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(PlayerBody) && m.GetParameters()[1].ParameterType == typeof(GameObject));
        private static void Postfix(PlayerBody __0, GameObject __1) => AishiHammerhead.BindVisual(__0, __1);
    }

    [HarmonyPatch(typeof(MovementContext), "SetInteractInHands")]
    internal static class AishiHammerheadNightAnimation
    {
        private static void Prefix(MovementContext __instance, ref EInteraction __0)
        {
            if (__0 != EInteraction.NightVisionOnGear && __0 != EInteraction.NightVisionOffGear) return;
            var world = Singleton<GameWorld>.Instance;
            if (world == null) return;
            foreach (var player in world.AllAlivePlayersList)
                if (player != null && ReferenceEquals(player.MovementContext, __instance) && AishiHammerhead.IsHelmet(player.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem))
                { __0 = EInteraction.HelmetRailGear; return; }
        }
    }
    [HarmonyPatch(typeof(EFT.GamePlayerOwner), nameof(EFT.GamePlayerOwner.TranslateCommand))]
    internal static class AishiHammerheadNightInput
    {
        private static System.Threading.Tasks.Task _pending;

        private static void Postfix(EFT.GamePlayerOwner __instance, EFT.InputSystem.ECommand __0, ref EFT.InputSystem.InputNode.ETranslateResult __result)
        {
            if (__0 != EFT.InputSystem.ECommand.ToggleGoggles || __result == EFT.InputSystem.InputNode.ETranslateResult.BlockAll) return;
            var player = __instance.Player;
            if (player == null || !player.IsYourPlayer || player.HealthController?.IsAlive != true || !player.StateIsSuitableForHandInput) return;
            var item = player.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem;
            if (item == null || item.TemplateId.ToString() != "7f3d1490eb7e5442947f28f5") return;
            __result = EFT.InputSystem.InputNode.ETranslateResult.BlockAll;
            if (_pending != null && !_pending.IsCompleted || player.HandsController?.InCanNotBeInterruptedOperation() == true) return;
            var toggle = item.GetItemComponent<NightVisionComponent>()?.Togglable;
            if (toggle != null && player.InventoryController != null) _pending = Toggle(toggle, player);
        }

        private static async System.Threading.Tasks.Task Toggle(TogglableComponent toggle, EFT.Player player)
        {
            try
            {
                var result = await player.InventoryController.TryRunNetworkTransaction(toggle.Set(!toggle.On, true, false), null);
                if (result.Succeed && player != null && player.HealthController?.IsAlive == true)
                    player.MovementContext.SetInteractInHands(EInteraction.HelmetRailGear);
            }
            catch (Exception error) { Debug.LogError("Aishi Hammerhead night vision toggle failed: " + error.GetBaseException().Message); }
        }
    }

}
