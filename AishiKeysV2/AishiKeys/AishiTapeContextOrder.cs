using System;
using System.Linq;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AishiKeys
{
    internal sealed class AishiContextButtonKey : MonoBehaviour
    {
        internal string Caption, Key;
        internal bool Saved;
        internal Color Default, Selected;
        internal Sprite Background;
        internal static string Normalize(string value) => new string ((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        internal bool Matches(string value) => Normalize(Caption) == value || Normalize(Key) == value;
    }

    [HarmonyPatch(typeof(ContextMenuButton), "Show")]
    internal static class AishiTapeContextOrder
    {
        private static readonly System.Reflection.FieldInfo Default = AccessTools.Field(typeof(SimpleContextMenuButton), "_defaultBackgroundColor"), Selected = AccessTools.Field(typeof(SimpleContextMenuButton), "_selectedBackgroundColor"), Background = AccessTools.Field(typeof(SimpleContextMenuButton), "_backgroundSprite"), BackgroundImage = AccessTools.Field(typeof(SimpleContextMenuButton), "_backgroundImage"), Icon = AccessTools.Field(typeof(ContextMenuButton), "_icon"), IconContainer = AccessTools.Field(typeof(ContextMenuButton), "_iconContainer"), IconLabel = AccessTools.Field(typeof(ContextMenuButton), "_iconLabel");
        private static void Prefix(ContextMenuButton __instance, string __0, string __1)
        {
            var key = __instance.GetComponent<AishiContextButtonKey>() ?? __instance.gameObject.AddComponent<AishiContextButtonKey>();
            if (!key.Saved)
            {
                key.Default = (Color)Default.GetValue(__instance);
                key.Selected = (Color)Selected.GetValue(__instance);
                key.Background = (Sprite)Background.GetValue(__instance);
                key.Saved = true;
            }

            Default.SetValue(__instance, key.Default);
            Selected.SetValue(__instance, key.Selected);
            Background.SetValue(__instance, key.Background);
            var image = (Image)BackgroundImage.GetValue(__instance);
            if (image != null)
                image.sprite = key.Background;
            key.Caption = __0;
            key.Key = __1;
        }

        private static void Postfix(ContextMenuButton __instance)
        {
            var key = __instance.GetComponent<AishiContextButtonKey>();
            bool reveal = key.Matches("aishirevealproperties") || key.Matches("revealproperties");
            bool tape = key.Matches("aishichangetape") || key.Matches("changetape");
            if (!reveal && !tape)
                return;
            Apply(__instance, reveal);
        }

        private static bool MarketSearch(AishiContextButtonKey key) => new[]
        {
            "filtersearch",
            "filterbyitem",
            "searchinmarket",
            "searchinmarketplace",
            "searchinflea",
            "searchinfleamarket",
            "ragfairsearch"
        }.Any(key.Matches);
        internal static void Apply(ContextMenuButton button, bool reveal)
        {
            var parent = button.transform.parent;
            if (parent == null)
                return;
            ContextMenuButton anchor = null, source = null, wishlist = null;
            foreach (Transform sibling in parent)
            {
                var other = sibling.GetComponent<AishiContextButtonKey>();
                var row = sibling.GetComponent<ContextMenuButton>();
                if (other == null || row == null || row == button)
                    continue;
                if (other.Matches(reveal ? "addtowishlist" : "tag"))
                    anchor = row;
                if (reveal ? MarketSearch(other) : other.Matches("tag"))
                    source = row;
                if (other.Matches("addtowishlist"))
                    wishlist = row;
            }

            if (anchor != null)
            {
                button.transform.SetAsLastSibling();
                button.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex() + (reveal ? 0 : 1));
            }

            var normal = new Color(.14f, .32f, .20f, 1f);
            var hover = new Color(.20f, .43f, .28f, 1f);
            Default.SetValue(button, normal);
            Selected.SetValue(button, hover);
            var background = (Image)BackgroundImage.GetValue(button);
            var wishlistBackground = wishlist == null ? null : (Image)BackgroundImage.GetValue(wishlist);
            if (background != null)
            {
                if (wishlistBackground?.sprite != null)
                {
                    Background.SetValue(button, wishlistBackground.sprite);
                    background.sprite = wishlistBackground.sprite;
                }

                background.color = normal;
            }

            var sourceIcon = source == null ? null : (Image)Icon.GetValue(source);
            var icon = (Image)Icon.GetValue(button);
            if (sourceIcon?.sprite != null && icon != null)
            {
                icon.sprite = sourceIcon.sprite;
                icon.gameObject.SetActive(true);
                var container = (GameObject)IconContainer.GetValue(button);
                if (container != null)
                    container.SetActive(true);
                var label = (TextMeshProUGUI)IconLabel.GetValue(button);
                if (label != null)
                    label.text = "";
            }
        }
    }
}
