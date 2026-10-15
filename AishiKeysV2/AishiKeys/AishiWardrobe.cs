using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AishiKeys
{
    internal static class AishiWardrobe
    {
        private static readonly FieldInfo IconField = AccessTools.Field(typeof(ClothingItem), "_clothingIcon");
        internal static Image Icon(ClothingItem item) => (Image)IconField.GetValue(item);
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(ClothingItem), "_clothingName");
        private static readonly FieldInfo DescriptionField = AccessTools.Field(typeof(ClothingItem), "_clothingDescription");
        private static PropertyInfo FilterReady;
        private static Harmony Harmony;
        private static bool FilterPatched;
        private static bool FilterRetryAttempted;
        internal static TMP_Text Name(ClothingItem item) => (TMP_Text)NameField.GetValue(item);
        internal static TMP_Text Description(ClothingItem item) => (TMP_Text)DescriptionField.GetValue(item);
        internal static void Enable()
        {
            var harmony = new Harmony("com.samc137.aishi.wardrobe");
            Harmony = harmony;
            harmony.Patch(AccessTools.Method(typeof(ClothingItem), "Init"), postfix: new HarmonyMethod(typeof(AishiWardrobe), nameof(Register)));
            harmony.Patch(AccessTools.Method(typeof(ClothingItem), "OnPointerClick"), prefix: new HarmonyMethod(typeof(AishiWardrobe), nameof(Click)));
            harmony.Patch(AccessTools.Method(typeof(ClothingItem), "method_0"), postfix: new HarmonyMethod(typeof(AishiWardrobe), nameof(IconReady)));
            harmony.Patch(AccessTools.Method(typeof(TacticalClothingView), "Close"), prefix: new HarmonyMethod(typeof(AishiWardrobe), nameof(Close)));
            EnableFilter();
        }

        private static void EnableFilter()
        {
            if (FilterPatched)
                return;
            var filterType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("ImprovedCustomizationUI.Ragman.ClothingFilterBar", false)).FirstOrDefault(type => type != null);
            var filter = filterType == null ? null : AccessTools.Method(filterType, "Apply", new[] { typeof(ClothingItem) });
            FilterReady = filterType == null ? null : AccessTools.Property(filterType, "Ready");
            if (filter != null && FilterReady?.GetGetMethod(true) != null)
            {
                Harmony.Patch(filter, postfix: new HarmonyMethod(typeof(AishiWardrobe), nameof(FilterApplied)));
                FilterPatched = true;
            }
        }

        private static void FilterApplied(object __instance, ClothingItem __0)
        {
            if (__0 == null || !(bool)FilterReady.GetValue(__instance))
                return;
            var group = __0?.GetComponentInParent<AishiWardrobeView>();
            if (group != null)
                group.FilterApplied(__0, __0.gameObject.activeSelf);
        }

        private static void IconReady(ClothingItem __instance)
        {
            var group = __instance.GetComponentInParent<AishiWardrobeView>();
            if (group != null)
                group.RefreshIcon(__instance);
        }

        private static void Register(ClothingItem __instance, Profile.TraderInfo __4)
        {
            if (__4?.Id != AishiDollarUi.TraderId)
                return;
            if (!FilterPatched && !FilterRetryAttempted)
            {
                FilterRetryAttempted = true;
                EnableFilter();
            }

            var view = __instance.GetComponentInParent<TacticalClothingView>();
            if (view == null)
                return;
            var group = view.GetComponent<AishiWardrobeView>() ?? view.gameObject.AddComponent<AishiWardrobeView>();
            group.Register(__instance);
        }

        private static bool Click(ClothingItem __instance, PointerEventData eventData)
        {
            var group = __instance.GetComponentInParent<AishiWardrobeView>();
            return group == null || !group.Open(__instance, eventData.button);
        }

        private static void Close(TacticalClothingView __instance)
        {
            var group = __instance.GetComponent<AishiWardrobeView>();
            if (group != null)
                group.Clear();
        }
    }

    public sealed class AishiWardrobeView : MonoBehaviour
    {
        private sealed class Row
        {
            internal ClothingItem Item;
            internal Transform Parent;
            internal string Family, Variant, Name, Description;
            internal int Rank, Order;
            internal GameObject Preview;
            internal AishiVariantSector[] Sectors;
            internal bool IconEnabled;
            internal bool FilterVisible = true;
        }

        private sealed class Page
        {
            internal string Family;
            internal GameObject Back;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<Transform, Page> _pages = new Dictionary<Transform, Page>();
        private bool _dirty;
        private Dictionary<string, AishiClothingCatalog.Entry> _groups;
        internal void RefreshIcon(ClothingItem item)
        {
            if (_dirty || item == null)
                return;
            var changed = _rows.Find(row => row.Item == item);
            if (changed == null || !changed.FilterVisible)
                return;
            var cover = _rows.Find(row => row.Parent == changed.Parent && row.Family == changed.Family && row.Preview != null);
            if (cover == null)
                return;
            var variants = _rows.Where(row => row.Parent == changed.Parent && row.Family == changed.Family && row.FilterVisible && row.Item != null).OrderBy(row => row.Rank).ThenBy(row => row.Order).ToArray();
            if (variants.Length > 1)
                Preview(cover, variants);
        }

        internal void Invalidate() => _dirty = true;
        internal void Register(ClothingItem item)
        {
            foreach (var old in _rows.Where(r => r.Item == item))
                RestorePreview(old);
            _rows.RemoveAll(r => r.Item == null || r.Item == item);
            if (_groups == null)
                _groups = AishiClothingGroups.Load();
            string id = item.Offer?.Offer?.SuiteId;
            _groups.TryGetValue(id ?? "", out var entry);
            var parent = item.transform.parent;
            if (!_pages.ContainsKey(parent))
                _pages.Add(parent, new Page());
            _rows.Add(new Row { Item = item, Parent = parent, Family = entry?.Family ?? id, Variant = entry?.Variant ?? "", Rank = entry?.Rank ?? 0, Name = AishiWardrobe.Name(item).text, Description = AishiWardrobe.Description(item).text, Order = item.transform.GetSiblingIndex(), IconEnabled = AishiWardrobe.Icon(item).enabled, FilterVisible = item.gameObject.activeSelf });
            _dirty = true;
        }

        internal bool Open(ClothingItem item, PointerEventData.InputButton button)
        {
            var row = _rows.Find(r => r.Item == item);
            if (row == null || !_pages.TryGetValue(row.Parent, out var page) || page.Family != null)
                return false;
            if (_rows.Count(r => r.Parent == row.Parent && r.Family == row.Family && r.FilterVisible) < 2)
                return false;
            if (button == PointerEventData.InputButton.Left)
            {
                page.Family = row.Family;
                _dirty = true;
            }

            return true;
        }

        internal void FilterApplied(ClothingItem item, bool visible)
        {
            var row = _rows.Find(r => r.Item == item);
            if (row == null || !_pages.TryGetValue(row.Parent, out var page))
                return;
            if (row.FilterVisible != visible)
            {
                row.FilterVisible = visible;
                _dirty = true;
            }

            bool shown = visible && (page.Family != null ? row.Family == page.Family : row == _rows.Where(r => r.Parent == row.Parent && r.Family == row.Family && r.FilterVisible).OrderBy(r => r.Rank).ThenBy(r => r.Order).FirstOrDefault());
            item.gameObject.SetActive(shown);
        }

        private void LateUpdate()
        {
            if (!_dirty)
                return;
            _dirty = false;
            _rows.RemoveAll(r => r.Item == null);
            foreach (var pair in _pages.ToArray())
            {
                if (pair.Key == null)
                {
                    _pages.Remove(pair.Key);
                    continue;
                }

                var page = pair.Value;
                var rows = _rows.Where(r => r.Parent == pair.Key).ToArray();
                foreach (var row in rows)
                    if (!row.FilterVisible)
                    {
                        row.Item.gameObject.SetActive(false);
                        RestorePreview(row);
                        AishiWardrobe.Name(row.Item).text = row.Name;
                        AishiWardrobe.Description(row.Item).text = row.Description;
                    }

                var families = rows.Where(r => r.FilterVisible).GroupBy(r => r.Family).OrderByDescending(g => g.Count() > 1).ThenByDescending(g => g.Count() > 1 ? g.Count() : 0).ThenBy(g => g.Count() > 1 ? g.Key : g.First().Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                foreach (var family in families)
                {
                    var variants = family.OrderBy(r => r.Rank).ThenBy(r => r.Order).ToArray();
                    for (int i = 0; i < variants.Length; i++)
                    {
                        var row = variants[i];
                        bool collapsed = page.Family == null && variants.Length > 1;
                        bool visible = page.Family == null ? i == 0 : row.Family == page.Family;
                        row.Item.gameObject.SetActive(visible);
                        AishiWardrobe.Name(row.Item).text = collapsed ? row.Family : row.Name;
                        AishiWardrobe.Description(row.Item).text = collapsed ? variants.Length + " VARIANTS  /  CLICK TO OPEN" : row.Description;
                        if (collapsed && i == 0)
                            Preview(row, variants);
                        else
                            RestorePreview(row);
                        if (page.Family == row.Family)
                            row.Item.transform.SetAsLastSibling();
                    }
                }

                if (page.Family != null && page.Back == null && rows.Length != 0)
                    page.Back = CreateBack(pair.Key, page, AishiWardrobe.Name(rows[0].Item));
                if (page.Back != null)
                {
                    page.Back.SetActive(page.Family != null);
                    page.Back.transform.SetAsFirstSibling();
                }

                if (page.Family == null)
                    foreach (var family in families)
                        foreach (var row in family.OrderBy(r => r.Rank).ThenBy(r => r.Order))
                            row.Item.transform.SetAsLastSibling();
                LayoutRebuilder.MarkLayoutForRebuild(pair.Key as RectTransform);
            }
        }

        private static void RestorePreview(Row row)
        {
            if (row.Preview != null)
            {
                row.Preview.SetActive(false);
                Destroy(row.Preview);
                row.Preview = null;
                row.Sectors = null;
            }

            if (row.Item != null)
                AishiWardrobe.Icon(row.Item).enabled = row.IconEnabled;
        }

        private static void Preview(Row row, Row[] variants)
        {
            var icon = AishiWardrobe.Icon(row.Item);
            if (row.Preview == null || row.Sectors == null || row.Sectors.Length != variants.Length)
            {
                RestorePreview(row);
                row.Preview = new GameObject("AishiVariantPreview", typeof(RectTransform));
                var holder = (RectTransform)row.Preview.transform;
                holder.SetParent(icon.transform, false);
                holder.anchorMin = Vector2.zero;
                holder.anchorMax = Vector2.one;
                holder.offsetMin = holder.offsetMax = Vector2.zero;
                row.Sectors = new AishiVariantSector[variants.Length];
                for (int i = 0; i < variants.Length; i++)
                {
                    var go = new GameObject("Variant" + i, typeof(RectTransform), typeof(AishiVariantSector));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(holder, false);
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    row.Sectors[i] = go.GetComponent<AishiVariantSector>();
                }

                var lines = new GameObject("Dividers", typeof(RectTransform), typeof(AishiVariantDividers));
                var lr = (RectTransform)lines.transform;
                lr.SetParent(holder, false);
                lr.anchorMin = Vector2.zero;
                lr.anchorMax = Vector2.one;
                lr.offsetMin = lr.offsetMax = Vector2.zero;
                lines.GetComponent<AishiVariantDividers>().Set(variants.Length);
            }

            icon.enabled = false;
            for (int i = 0; i < variants.Length; i++)
                row.Sectors[i].Set(variants[i].Item.ClothingIcon ?? row.Item.ClothingIcon, i, variants.Length);
        }

        private GameObject CreateBack(Transform parent, Page page, TMP_Text sample)
        {
            var go = new GameObject("AishiVariantsBack", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.075f, .085f, .085f, .96f);
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 38;
            layout.minHeight = 38;
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(go.transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 0);
            rt.offsetMax = new Vector2(-8, 0);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.font = sample.font;
            text.fontSize = 17;
            text.color = new Color(.76f, .72f, .56f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = "<  ALL CLOTHING";
            text.raycastTarget = false;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                page.Family = null;
                _dirty = true;
            });
            return go;
        }

        internal void Clear()
        {
            foreach (var row in _rows)
                if (row.Item != null)
                {
                    RestorePreview(row);
                    row.Item.gameObject.SetActive(true);
                    AishiWardrobe.Name(row.Item).text = row.Name;
                    AishiWardrobe.Description(row.Item).text = row.Description;
                }

            foreach (var page in _pages.Values)
                if (page.Back != null)
                    Destroy(page.Back);
            _rows.Clear();
            _pages.Clear();
            _groups = null;
            _dirty = false;
        }

        private void OnDestroy() => Clear();
    }
}
