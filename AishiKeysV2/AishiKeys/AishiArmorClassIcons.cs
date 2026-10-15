using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AishiKeysPro;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace AishiKeys
{
    internal static class AishiArmorClassIcons
    {
        private const string ResourceKey = "Mod Types/icon_type_mod_armor_plate_7";
        private const string AssetName = "AishiArmorClasses";
        private static readonly Type CacheType = typeof(ItemViewStats).Assembly.GetType("EFT.Utilities.ResourcesCache") ?? typeof(ItemViewStats).Assembly.GetType("CacheResourcesPopAbstractClass");
        private static readonly FieldInfo Storage = CacheType?.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SingleOrDefault(f => f.FieldType == typeof(Dictionary<string, object>));
        private static readonly int TextIconHash = TMP_TextUtilities.GetSimpleHashCode("armor_classes_7");
        private static Texture2D _texture;
        private static Sprite _sprite;
        private static TMP_SpriteAsset _textAsset;
        private static bool _warned;
        internal static void Enable()
        {
            EnsureInventoryIcon();
            TMP_Text.OnSpriteAssetRequest += ResolveTextAsset;
            var harmony = new Harmony("com.samc137.aishi.armor-class-vii");
            var inventoryMethod = AccessTools.Method(typeof(ItemViewStats), "SetArmorPlateIcon") ?? AccessTools.Method(typeof(ItemViewStats), "method_1");
            harmony.Patch(inventoryMethod, prefix: new HarmonyMethod(typeof(AishiArmorClassIcons), nameof(BeforeInventoryIcon)));
            harmony.Patch(AccessTools.Method(typeof(TMP_SpriteAsset), "SearchForSpriteByHashCode"), prefix: new HarmonyMethod(typeof(AishiArmorClassIcons), nameof(ResolveMissingClassIcon)));
        }

        private static void EnsureInventoryIcon()
        {
            if (_sprite == null)
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aishi.ArmorClassVII.png");
                if (stream == null)
                    throw new InvalidOperationException("Embedded class VII sprite is missing.");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = AssetName,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!ImageConversion.LoadImage(_texture, bytes.ToArray(), false))
                    throw new InvalidOperationException("Cannot decode class VII sprite.");
                _sprite = Sprite.Create(_texture, new Rect(0, 0, _texture.width, _texture.height), new Vector2(.5f, .5f), 100f);
                _sprite.name = "icon_type_mod_armor_plate_7";
                _sprite.hideFlags = HideFlags.HideAndDontSave;
            }

            var cache = Storage?.GetValue(null) as IDictionary<string, object>;
            if (cache == null)
                throw new InvalidOperationException("Native sprite cache was not found.");
            cache[ResourceKey] = _sprite;
        }

        private static void BeforeInventoryIcon()
        {
            try
            {
                EnsureInventoryIcon();
            }
            catch (Exception e)
            {
                Warn(e);
            }
        }

        private static TMP_SpriteAsset EnsureTextAsset()
        {
            if (_textAsset != null)
                return _textAsset;
            EnsureInventoryIcon();
            TMP_SpriteAsset native = null;
            TMP_SpriteCharacter vi = null;
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
            {
                var index = asset.GetSpriteIndexFromName("armor_classes_6");
                if (index < 0)
                    continue;
                native = asset;
                vi = asset.spriteCharacterTable[index];
                break;
            }

            var shader = native?.material?.shader ?? Shader.Find("TextMeshPro/Sprite");
            if (shader == null)
                throw new InvalidOperationException("Native text sprite shader is not loaded.");
            var textAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            textAsset.name = AssetName;
            textAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(AssetName);
            textAsset.hideFlags = HideFlags.HideAndDontSave;
            AccessTools.Field(typeof(TMP_SpriteAsset), "m_Version").SetValue(textAsset, "1.1.0");
            if (native != null)
                AccessTools.Field(typeof(TMP_SpriteAsset), "m_FaceInfo").SetValue(textAsset, native.faceInfo);
            textAsset.spriteSheet = _texture;
            textAsset.material = native?.material != null ? new Material(native.material) : new Material(shader);
            textAsset.material.name = AssetName + " Material";
            textAsset.material.mainTexture = _texture;
            textAsset.material.hideFlags = HideFlags.HideAndDontSave;
            var metrics = vi?.glyph?.metrics ?? new GlyphMetrics(18, 18, 0, 15, 18);
            var glyph = new TMP_SpriteGlyph(0, metrics, new GlyphRect(0, 0, _texture.width, _texture.height), vi?.glyph?.scale ?? 1f, 0, _sprite);
            var character = new TMP_SpriteCharacter(0xFFFE, textAsset, glyph)
            {
                name = "armor_classes_7",
                scale = vi?.scale ?? 1f
            };
            textAsset.spriteGlyphTable.Add(glyph);
            textAsset.spriteCharacterTable.Add(character);
            textAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            textAsset.UpdateLookupTables();
            _textAsset = textAsset;
            return _textAsset;
        }

        private static TMP_SpriteAsset ResolveTextAsset(int hash, string name)
        {
            if (name != AssetName)
                return null;
            try
            {
                return EnsureTextAsset();
            }
            catch (Exception e)
            {
                Warn(e);
                return null;
            }
        }

        private static bool ResolveMissingClassIcon(TMP_SpriteAsset __0, int __1, ref int __3, ref TMP_SpriteAsset __result)
        {
            if (__1 != TextIconHash || (__0 != null && __0.GetSpriteIndexFromName("armor_classes_7") >= 0))
                return true;
            try
            {
                __result = EnsureTextAsset();
                __3 = 0;
                return false;
            }
            catch (Exception e)
            {
                Warn(e);
            }

            return true;
        }

        private static void Warn(Exception e)
        {
            if (_warned)
                return;
            _warned = true;
            AishiKeysMod.Logger?.LogWarning("Aishi class VII icon: " + e.Message);
        }
    }
}
