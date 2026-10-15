using System;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AishiKeys
{
    internal sealed class MusicButton
    {
        private readonly DefaultUIButton _native;
        private readonly Button _fallback;
        internal readonly TMP_Text Label;
        internal MusicButton(DefaultUIButton native, TMP_Text label)
        {
            _native = native;
            Label = label;
        }

        internal MusicButton(Button fallback, TMP_Text label)
        {
            _fallback = fallback;
            Label = label;
        }

        internal bool interactable
        {
            set
            {
                if (_native != null)
                    _native.Interactable = value;
                else
                    _fallback.interactable = value;
                if (Label != null)
                    Label.color = value ? new Color(.75f, .71f, .56f, 1f) : new Color(.43f, .45f, .42f, 1f);
            }
        }
    }

    internal static class AishiNativeMusicButton
    {
        private static readonly FieldInfo Header = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static DefaultUIButton templateCache;
        private static void Fit(RectTransform rect, Transform root)
        {
            for (var current = rect; current != null && current != root; current = current.parent as RectTransform)
            {
                current.anchorMin = Vector2.zero;
                current.anchorMax = Vector2.one;
                current.pivot = new Vector2(.5f, .5f);
                current.offsetMin = current.offsetMax = Vector2.zero;
                current.localScale = Vector3.one;
            }
        }

        internal static MusicButton Create(RectTransform parent, string title, Action action, TMP_FontAsset font)
        {
            var template = templateCache;
            if (template == null)
                templateCache = template = Resources.FindObjectsOfTypeAll<DefaultUIButton>().FirstOrDefault(b => b != null && b.gameObject.scene.IsValid() && b.GetComponentInParent<AishiMusicPanel>() == null && b.name != "NativeTarkovButton" && b.GetComponents<MonoBehaviour>().All(c => c == null || c.GetType().Namespace == "EFT.UI" || c.GetType().Namespace == "UnityEngine.UI"));
            if (template == null)
                return null;
            var native = UnityEngine.Object.Instantiate(template, parent, false);
            native.gameObject.SetActive(false);
            native.name = "NativeTarkovButton";
            foreach (string field in new[]
            {
                "OnClick",
                "OnMouseOver",
                "OnMouseOut"
            }

            )
                AccessTools.Field(typeof(DefaultUIButton), field).SetValue(native, new UnityEvent());
            native.OnClick.AddListener(() => action());
            native.SetIcon(null, null);
            native.SetTooltips(string.Empty, string.Empty);
            AccessTools.Field(typeof(DefaultUIButton), "_minWidth").SetValue(native, 0f);
            foreach (var fitter in native.GetComponentsInChildren<ContentSizeFitter>(true))
                fitter.enabled = false;
            foreach (var fitter in native.GetComponentsInChildren<AspectRatioFitter>(true))
                fitter.enabled = false;
            foreach (var group in native.GetComponentsInChildren<LayoutGroup>(true))
                group.enabled = false;
            var sizeLabel = AccessTools.Field(typeof(DefaultUIButton), "_sizeLabel").GetValue(native) as TMP_Text;
            if (sizeLabel != null)
                sizeLabel.enabled = false;
            native.SetRawText(title, 17);
            native.Interactable = true;
            parent.gameObject.AddComponent<RectMask2D>();
            var rt = (RectTransform)native.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var layout = native.GetComponent<LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;
            native.gameObject.SetActive(true);
            var label = Header?.GetValue(native) as TMP_Text;
            if (label == null)
                label = native.GetComponentInChildren<TMP_Text>(true);
            foreach (var graphic in native.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.gameObject.activeSelf)
                    continue;
                Fit(graphic.rectTransform, native.transform);
                if (graphic is MaskableGraphic masked)
                    masked.maskable = true;
            }

            var animation = native.GetComponentInChildren<DefaultUIButtonAnimation>(true);
            if (animation != null && animation.Background != null)
                Fit(animation.Background, native.transform);
            if (label != null)
                label.enabled = false;
            var labelObject = new GameObject("MusicButtonLabel", typeof(RectTransform));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(parent, false);
            Fit(labelRect, parent);
            var visibleLabel = labelObject.AddComponent<TextMeshProUGUI>();
            visibleLabel.font = font;
            visibleLabel.fontSize = 17;
            visibleLabel.color = new Color(.75f, .71f, .56f, 1f);
            visibleLabel.richText = false;
            visibleLabel.text = title;
            visibleLabel.enableWordWrapping = false;
            visibleLabel.overflowMode = TextOverflowModes.Ellipsis;
            visibleLabel.alignment = TextAlignmentOptions.Center;
            visibleLabel.margin = new Vector4(4, 0, 4, 0);
            visibleLabel.raycastTarget = false;
            visibleLabel.maskable = true;
            native.OnMouseOver.AddListener(() => visibleLabel.color = new Color(.12f, .14f, .13f, 1f));
            native.OnMouseOut.AddListener(() => visibleLabel.color = new Color(.75f, .71f, .56f, 1f));
            return new MusicButton(native, visibleLabel);
        }
    }
}
