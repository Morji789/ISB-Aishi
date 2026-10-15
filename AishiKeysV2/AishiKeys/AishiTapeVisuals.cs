using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
#if SPT41
using Factory = EFT.ObjectsFactory;
using NativeHash = IconsHash;
using TagResult = EFT.InventoryLogic.SetTagResult;

#else
using Factory=PoolManagerClass;
using NativeHash=GClass928;
using TagResult=GClass3441;
#endif
namespace AishiKeys
{
    internal static class AishiTapeVisuals
    {
        private static readonly ConditionalWeakTable<Item, List<WeakReference>> Bindings = new ConditionalWeakTable<Item, List<WeakReference>>();
        internal static void Enable(Harmony harmony)
        {
            foreach (var method in typeof(Factory).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var p = method.GetParameters();
                if (p.Length == 0 || p[0].ParameterType != typeof(Item))
                    continue;
                if (method.ReturnType == typeof(Task<GameObject>))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(AsyncCreated)));
                else if (method.ReturnType == typeof(GameObject))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(Created)));
            }

            harmony.Patch(AccessTools.Method(typeof(NativeHash), 
#if SPT41
            "HashForItem"), postfix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(Hash)));
#else
                "smethod_1"),postfix:new HarmonyMethod(typeof(AishiTapeVisuals),nameof(Hash)));
#endif
            harmony.Patch(AccessTools.Method(typeof(TagResult), "RaiseEvents"), postfix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(TagChanged)));
            harmony.Patch(AccessTools.Method(typeof(EditTagWindow), "GetColor"), prefix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(TagTint)));
            harmony.Patch(AccessTools.Method(typeof(TagColorsPanel), "Show"), prefix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(TagPalette)));
            harmony.Patch(AccessTools.Method(typeof(TagComponent), "Set"), prefix: new HarmonyMethod(typeof(AishiTapeVisuals), nameof(KeepTapeOnReset)));
        }

        private static void AsyncCreated(Item __0, ref Task<GameObject> __result)
        {
            if (AishiMagazineVariants.IsTape(__0))
                __result = PaintCompleted(__result, __0);
        }

        private static async Task<GameObject> PaintCompleted(Task<GameObject> task, Item item)
        {
            var result = await task;
            Apply(result, item);
            return result;
        }

        private static void Created(Item __0, GameObject __result)
        {
            if (AishiMagazineVariants.IsTape(__0))
                Apply(__result, __0);
        }

        private static void Apply(GameObject obj, Item item)
        {
            if (obj == null)
                return;
            var binding = obj.GetComponent<AishiTapeVisualBinding>() ?? obj.AddComponent<AishiTapeVisualBinding>();
            if (binding.Bind(item))
            {
                var list = Bindings.GetOrCreateValue(item);
                bool found = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var other = list[i].Target as AishiTapeVisualBinding;
                    if (other == null || !other.IsBound(item))
                        list.RemoveAt(i);
                    else if (ReferenceEquals(other, binding))
                        found = true;
                }

                if (!found)
                    list.Add(new WeakReference(binding));
            }
        }

        private static void Hash(Item __0, ref int __result)
        {
            if (AishiMagazineVariants.IsTape(__0))
                unchecked
                {
                    __result = (__result * 397) ^ ((AishiMagazineVariants.ColorCode(__0) + 1) * 104729) ^ 0x5a715e;
                }
        }

        private static void TagChanged(TagResult __instance, CommandStatus __1)
        {
            var item = __instance.Tag.Item;
            if ((int)__1 != 1 || !AishiMagazineVariants.IsTape(item))
                return;
            if (Bindings.TryGetValue(item, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var binding = list[i].Target as AishiTapeVisualBinding;
                    if (binding == null || !binding.IsBound(item))
                        list.RemoveAt(i);
                    else
                        binding.Paint();
                }
            }

            item.Version++;
            item.RaiseRefreshEvent(true, false);
        }

        private static bool TagTint(int __0, ref Color __result)
        {
            if (!AishiMagazineVariants.IsColor(__0))
                return true;
            __result = AishiMagazineVariants.Tint(__0);
            return false;
        }

        private static void KeepTapeOnReset(TagComponent __instance, string __0, ref int __1)
        {
            if (__1 == 0 && string.IsNullOrEmpty(__0) && AishiMagazineVariants.IsColor(__instance.Color) && AishiMagazineVariants.IsTape(__instance.Item))
                __1 = __instance.Color;
        }

        private static readonly FieldInfo WindowTag = typeof(EditTagWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single(f => f.FieldType == typeof(TagComponent));
        private static void TagPalette(TagColorsPanel __instance, ref Color[] __0, ref int __1, ref Action<ColorView, int> __2)
        {
            var window = __instance.GetComponentInParent<EditTagWindow>();
            var tag = window != null ? WindowTag.GetValue(window) as TagComponent : null;
            if (tag == null || !AishiMagazineVariants.IsTape(tag.Item))
                return;
            __0 = AishiMagazineVariants.Palette;
            __1 = AishiMagazineVariants.ColorCode(tag.Item) - AishiMagazineVariants.FirstColor;
            var callback = __2;
            __2 = (view, index) => callback?.Invoke(view, index + AishiMagazineVariants.FirstColor);
        }
    }

    public sealed class AishiTapeVisualBinding : MonoBehaviour
    {
        private Item item;
        private Renderer[] bands;
        private MaterialPropertyBlock block;
        private static readonly int Tint = Shader.PropertyToID("_Color"), BaseTint = Shader.PropertyToID("_BaseTintColor");
        internal bool Bind(Item value)
        {
            bool changed = !ReferenceEquals(item, value);
            item = value;
            if (bands == null)
                bands = GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.name.StartsWith("ISB Cloth Tape LOD", StringComparison.Ordinal)).ToArray();
            if (block == null)
                block = new MaterialPropertyBlock();
            Paint();
            return changed;
        }

        internal bool IsBound(Item value) => ReferenceEquals(item, value);
        internal void Paint()
        {
            if (item == null)
                return;
            var color = AishiMagazineVariants.Tint(AishiMagazineVariants.ColorCode(item));
            var neutral = new Color(224f / 255f, 224f / 255f, 224f / 255f, 1f);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                color = color.linear;
                neutral = neutral.linear;
            }

            var tint = new Vector4(color.r / neutral.r, color.g / neutral.g, color.b / neutral.b, 1f);
            foreach (var band in bands)
            {
                if (band == null)
                    continue;
                band.GetPropertyBlock(block);
                block.SetVector(Tint, tint);
                block.SetVector(BaseTint, Vector4.one);
                band.SetPropertyBlock(block);
            }
        }

        private void OnDisable()
        {
            item = null;
        }
    }
}
