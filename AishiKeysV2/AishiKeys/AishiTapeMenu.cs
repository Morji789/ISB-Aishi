using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.UI;
using TMPro;
using UnityEngine;
#if SPT41
using NativeInteractions = EFT.UI.ContextInteractions<AishiKeys.TapeChoice>;
#else
using NativeInteractions = ItemInfoInteractionsAbstractClass<AishiKeys.TapeChoice>;

#endif
namespace AishiKeys
{
    public enum TapeChoice
    {
        A,
        B,
        C,
        D,
        E,
        F,
        G,
        H,
        I,
        J,
        K,
        L,
        M,
        N,
        O,
        P
    }

    internal sealed class TapeOption
    {
        internal string Label, Color;
        internal bool Selected;
        internal Action Select;
    }

    internal sealed class TapeInteractions : NativeInteractions
    {
        private readonly TapeOption[] options;
        private readonly Action close;
        private bool executed;
        internal TapeInteractions(TapeOption[] options, Action close) : base(ItemUiContext.Instance)
        {
            this.options = options;
            this.close = close;
        }

        public override IEnumerable<TapeChoice> AllInteractions => Enumerable.Range(0, options.Length).Select(i => (TapeChoice)i);
        public override bool ExaminationRequired => false;

        public override bool IsActive(TapeChoice key) => (int)key >= 0 && (int)key < options.Length;
        public override IResult IsInteractive(TapeChoice key) => new SuccessfulResult();
        public override void ExecuteInteractionInternal(TapeChoice key)
        {
            if (executed || !IsActive(key))
                return;
            executed = true;
            var action = options[(int)key].Select;
            close();
            action?.Invoke();
        }
    }

    public sealed class AishiTapeMenu : MonoBehaviour
    {
        private SimpleContextMenu menu;
        private static AishiTapeMenu owner;
        private readonly Dictionary<string, Sprite> swatchCache = new Dictionary<string, Sprite>();
        private readonly List<UnityEngine.Object> icons = new List<UnityEngine.Object>();
        internal static GameObject Badge(Transform parent, TMP_FontAsset font, Action click)
        {
            var go = new GameObject("AishiTapes", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var button = AishiNativeMusicButton.Create(rect, "TAPES", click, font);
            if (button?.Label != null)
                button.Label.fontSize = 10;
            return go;
        }

        internal void Open(string model, TapeOption[] options, TMP_FontAsset font)
        {
            Clear();
            var native = ItemUiContext.Instance?.ContextMenu;
            if (native == null || options.Length == 0)
                return;
            if (native.gameObject.activeSelf)
                native.Close();
            owner?.Clear();
            menu = native;
            owner = this;
            var labels = new Dictionary<TapeChoice, string>();
            var swatches = new Dictionary<TapeChoice, Sprite>();
            for (int i = 0; i < options.Length; i++)
            {
                var option = options[i];
                var key = (TapeChoice)i;
                labels[key] = option.Label + (option.Selected ? "  •" : "");
                if (!swatchCache.TryGetValue(option.Color, out var sprite))
                {
                    ColorUtility.TryParseHtmlString(option.Color, out var color);
                    var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    texture.SetPixels(Enumerable.Repeat(color, 256).ToArray());
                    texture.Apply(false, true);
                    sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f));
                    icons.Add(sprite);
                    icons.Add(texture);
                    swatchCache[option.Color] = sprite;
                }

                swatches[key] = sprite;
            }

            menu.Show((Vector2)Input.mousePosition, new TapeInteractions(options, Clear), labels, swatches);
            menu.transform.SetAsLastSibling();
        }

        internal void Clear()
        {
            var previous = menu;
            menu = null;
            if (owner == this)
            {
                owner = null;
                if (previous != null && previous.gameObject.activeSelf)
                    previous.Close();
            }
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear();
            foreach (var icon in icons)
                Destroy(icon);
            icons.Clear();
            swatchCache.Clear();
        }
    }
}
