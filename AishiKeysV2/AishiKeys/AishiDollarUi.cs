using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AishiShared;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Insurance;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using InsuranceCompany = EFT.UI.Insurance.InsuranceCompany;
using InsurancePrice = EFT.UI.Insurance.InsuranceCompany.PriceData;
using CurrencyIcons = EFT.UI.DragAndDrop.GuiHelper;

namespace AishiKeys
{
    internal static class AishiDollarUi
    {
        internal const string TraderId = "690766de550bc322a810ea1e";
        internal static void Enable()
        {
            AishiPatchBootstrap.EnableGroup(true);
            AishiUiFixesCompatibility.Enable();
        }

        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, FieldInfo> InsuranceFields = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, PropertyInfo> RepairerIds = new Dictionary<Type, PropertyInfo>();
        private static readonly PropertyInfo CurrentRepairer = AccessTools.Property(typeof(RepairerParametersPanel), "CurrentRepairer");
        private static readonly ConditionalWeakTable<Component, AishiCurrencyView> Views = new ConditionalWeakTable<Component, AishiCurrencyView>();
        internal static InsuranceCompany Insurance(object panel)
        {
            var type = panel.GetType();
            if (!InsuranceFields.TryGetValue(type, out var field))
            {
                field = type.GetFields(Fields).FirstOrDefault(f => f.FieldType == typeof(InsuranceCompany));
                InsuranceFields[type] = field;
            }

            return field?.GetValue(panel) as InsuranceCompany;
        }

        internal static bool IsAishi(object panel)
        {
            if (panel is InsuranceCompany company)
                return company.SelectedInsurerId == TraderId;
            if (panel is RepairerParametersPanel)
            {
                var repairer = CurrentRepairer?.GetValue(panel, null);
                if (repairer == null)
                    return false;
                var type = repairer.GetType();
                if (!RepairerIds.TryGetValue(type, out var property))
                    RepairerIds[type] = property = AccessTools.Property(type, "RepairerId");
                return Convert.ToString(property?.GetValue(repairer, null)) == TraderId;
            }

            return Insurance(panel)?.SelectedInsurerId == TraderId;
        }

        public static ECurrencyType Currency(object panel) => IsAishi(panel) ? ECurrencyType.USD : ECurrencyType.RUB;
        internal static bool Special(ItemTemplate template, InsuranceCompany company) => template.InsuranceDisabled && company.SelectedInsurerId == TraderId && IsbInsuranceCatalog.Ids.Contains(template.StringId);
        public static bool Disabled(ItemTemplate template, InsuranceCompany company) => template.InsuranceDisabled && !Special(template, company);
        public static bool Always(ItemTemplate template, InsuranceCompany company) => template.IsAlwaysAvailableForInsurance || Special(template, company);
        internal static IEnumerable<CodeInstruction> MoneyLookups(IEnumerable<CodeInstruction> instructions, bool insurance)
        {
            var list = instructions.ToList();
            int changed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                bool lookup = i + 1 < list.Count && list[i + 1].operand is MethodInfo method && method.Name == "get_Item" && method.DeclaringType == typeof(Dictionary<ECurrencyType, int>);
                bool tooltip = insurance && i + 2 < list.Count && list[i + 1].opcode == OpCodes.Ldc_I4_0 && list[i + 2].opcode == OpCodes.Ldarg_0;
                if (instruction.opcode == OpCodes.Ldc_I4_0 && (lookup || tooltip))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AishiDollarUi), nameof(Currency)));
                    changed++;
                }
                else
                    yield return instruction;
            }

            if (changed != (insurance ? 4 : 2))
                throw new InvalidOperationException("Aishi: native money UI shape changed (" + changed + ").");
        }

        internal static void Attach(Component panel)
        {
            if (panel != null)
                Views.GetValue(panel, value => new AishiCurrencyView(value)).Refresh();
        }
    }

    [HarmonyPatch]
    internal static class AishiInsuranceEligibility
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(InsuranceCompany).GetMethods().Where(m => m.Name == "ItemTypeAvailableForInsurance");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int changed = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.DeclaringType == typeof(ItemTemplate) && (field.Name == "InsuranceDisabled" || field.Name == "IsAlwaysAvailableForInsurance"))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AishiDollarUi), field.Name == "InsuranceDisabled" ? "Disabled" : "Always"));
                    changed++;
                }
                else
                    yield return instruction;
            }

            if (changed != 2)
                throw new InvalidOperationException("Aishi: native insurance eligibility shape changed.");
        }
    }

    [HarmonyPatch(typeof(InsuranceCompany), "get_OverallPrice")]
    internal static class AishiInsuranceQueueGuard
    {
        private static void Postfix(InsuranceCompany __instance, ref InsurancePrice __result)
        {
            if (__instance.SelectedInsurerId != AishiDollarUi.TraderId && __instance.AllItemsToInsure.Any(i => i.Item.Template.InsuranceDisabled && IsbInsuranceCatalog.Ids.Contains(i.Item.Template.StringId)))
                __result.Error = InsuranceCompany.EInsuranceError.InvalidItem;
        }
    }

    [HarmonyPatch(typeof(InsurerParametersPanel), "UpdateLabels")]
    internal static class AishiInsuranceMoneyUi
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => AishiDollarUi.MoneyLookups(instructions, true);
        private static void Postfix(InsurerParametersPanel __instance) => AishiDollarUi.Attach(__instance);
    }

    [HarmonyPatch(typeof(RepairerParametersPanel), "UpdateTraderLabels")]
    internal static class AishiRepairMoneyUi
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => AishiDollarUi.MoneyLookups(instructions, false);
        private static void Postfix(RepairerParametersPanel __instance) => AishiDollarUi.Attach(__instance);
    }

    [HarmonyPatch(typeof(ItemToInsurePanel), "UpdatePrice", new[] { typeof(int), typeof(int) })]
    internal static class AishiInsuranceItemCurrency
    {
        private static void Postfix(ItemToInsurePanel __instance) => AishiDollarUi.Attach(__instance);
    }

    [HarmonyPatch(typeof(ItemToInsurePanel), "OnTraderChangedHandler")]
    internal static class AishiInsuranceItemTraderCurrency
    {
        private static void Postfix(ItemToInsurePanel __instance) => AishiDollarUi.Attach(__instance);
    }

    internal sealed class AishiCurrencyView
    {
        private static readonly Dictionary<Sprite, Sprite> DollarSprites = new Dictionary<Sprite, Sprite>();
        private static bool iconsLoaded;
        private readonly Component panel;
        private readonly TMP_Text[] labels;
        private readonly Image[] icons;
        private readonly Image[] repairIcons;
        private readonly Dictionary<Image, Sprite> originals = new Dictionary<Image, Sprite>();
        private readonly Dictionary<TMP_Text, string> originalText = new Dictionary<TMP_Text, string>();
        private readonly Dictionary<TMP_Text, string> dollarText = new Dictionary<TMP_Text, string>();
        private static readonly Dictionary<Type, FieldInfo[]> TextFields = new Dictionary<Type, FieldInfo[]>();
        internal AishiCurrencyView(Component panel)
        {
            this.panel = panel;
            var type = panel.GetType();
            if (!TextFields.TryGetValue(type, out var fields))
                TextFields[type] = fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.Name == "_cashInStash" || f.Name == "_sumToPay" || f.Name == "_minPayment" || f.Name == "_itemPriceLabel").ToArray();
            labels = fields.Select(f => f.GetValue(panel)).OfType<TMP_Text>().ToArray();
            icons = panel is ItemToInsurePanel ? new Image[0] : panel.GetComponentsInChildren<Image>(true);
            repairIcons = panel is RepairerParametersPanel ? new[]
            {
                "_cashInStashIcon",
                "_sumToPayIcon"
            }.Select(n => AccessTools.Field(type, n)?.GetValue(panel)).OfType<Image>().ToArray() : new Image[0];
        }

        private static void LoadIcons()
        {
            if (iconsLoaded)
                return;
            var rubIcon = CurrencyIcons.GetCurrencyIcon(ECurrencyType.RUB);
            var usdIcon = CurrencyIcons.GetCurrencyIcon(ECurrencyType.USD);
            if (rubIcon == null || usdIcon == null)
                return;
            DollarSprites[rubIcon] = usdIcon;
            foreach (var source in Resources.FindObjectsOfTypeAll<StaticIcons>())
                foreach (var map in new[]
                {
                    source.CurrencyTypeSmallSprites,
                    source.CurrencyTypeBigSprites
                }

                )
                    if (map != null && map.TryGetValue(ECurrencyType.RUB, out var rub) && rub != null && map.TryGetValue(ECurrencyType.USD, out var dollar) && dollar != null)
                        DollarSprites[rub] = dollar;
            iconsLoaded = true;
        }

        internal void Refresh()
        {
            if (panel == null)
                return;
            bool usd = AishiDollarUi.IsAishi(panel);
            foreach (var label in labels)
            {
                if (label == null)
                    continue;
                if (usd)
                {
                    if (dollarText.TryGetValue(label, out var shown) && label.text == shown)
                        continue;
                    originalText[label] = label.text;
                    var normalized = label.text.Replace("₽", "").Trim();
                    if (!normalized.StartsWith("$ ", StringComparison.Ordinal))
                        normalized = "$ " + normalized;
                    dollarText[label] = normalized;
                    label.text = normalized;
                }
                else
                {
                    if (dollarText.TryGetValue(label, out var shown) && label.text == shown && originalText.TryGetValue(label, out var original))
                        label.text = original;
                    dollarText.Remove(label);
                    originalText.Remove(label);
                }
            }

            if (icons.Length == 0 && repairIcons.Length == 0)
                return;
            if (usd)
            {
                LoadIcons();
                foreach (var icon in icons)
                    if (icon != null && icon.sprite != null && DollarSprites.TryGetValue(icon.sprite, out var dollar))
                    {
                        if (!originals.ContainsKey(icon))
                            originals[icon] = icon.sprite;
                        icon.sprite = dollar;
                    }

                if (repairIcons.Length != 0)
                {
                    var dollar = CurrencyIcons.GetCurrencyIcon(ECurrencyType.USD);
                    foreach (var icon in repairIcons)
                    {
                        if (icon == null || dollar == null || icon.sprite == dollar)
                            continue;
                        if (!originals.ContainsKey(icon))
                            originals[icon] = icon.sprite;
                        if (icon.sprite != null)
                            DollarSprites[icon.sprite] = dollar;
                        icon.sprite = dollar;
                    }
                }
            }
            else
            {
                foreach (var pair in originals)
                    if (pair.Key != null && pair.Value != null && DollarSprites.TryGetValue(pair.Value, out var dollar) && pair.Key.sprite == dollar)
                        pair.Key.sprite = pair.Value;
                originals.Clear();
            }
        }
    }
}
