using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace AishiKeys
{
    internal sealed class MagazineFinish
    {
        public string Label { get; set; }
        public string Color { get; set; }
        public int TagColor { get; set; }
    }

    internal sealed class MagazineFamily
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Tape { get; set; }
        public string Kind { get; set; }
        public MagazineFinish[] Variants { get; set; }
    }

    internal static class AishiMagazineVariants
    {
        internal const int FirstColor = 100, ColorCount = 9, Gray = 103;
        internal static readonly Dictionary<string, MagazineFamily> Families = new Dictionary<string, MagazineFamily>();
        internal static readonly Color[] Palette = new Color[ColorCount];
        internal static bool IsColor(int color) => color >= FirstColor && color < FirstColor + ColorCount;
        internal static bool IsTape(Item item) => item != null && Families.ContainsKey(item.TemplateId);
        internal static int ColorCode(Item item)
        {
            var tag = item.GetItemComponent<TagComponent>();
            return tag != null && IsColor(tag.Color) ? tag.Color : Gray;
        }

        internal static Color Tint(int code) => Palette[IsColor(code) ? code - FirstColor : Gray - FirstColor];
        internal static void Enable()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(AishiMagazineVariants).Assembly.Location), "MagazineVariants.json");
            foreach (var group in JsonConvert.DeserializeObject<MagazineFamily[]>(File.ReadAllText(path)))
            {
                if (group.Kind != "tape" || string.IsNullOrEmpty(group.Tape))
                    continue;
                Families.Add(group.Tape, group);
                foreach (var finish in group.Variants.Where(v => IsColor(v.TagColor)))
                {
                    if (!ColorUtility.TryParseHtmlString(finish.Color, out var color))
                        throw new InvalidDataException("Invalid tape color");
                    Palette[finish.TagColor - FirstColor] = color;
                }
            }

            AishiTapeVisuals.Enable(new Harmony("com.samc137.aishi.tape-state"));
            AishiKeysPro.AishiKeysMod.Logger.LogInfo("Tape state: " + Families.Values.Distinct().Count() + " models; fixed native offers, per-item color.");
        }
    }
}
