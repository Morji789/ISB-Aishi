using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Insurance;
using InsuranceCompany = InsuranceCompanyClass;
using InsuredItem = ItemClass;
using HarmonyLib;

namespace AishiKeys
{
    internal static class AishiUiFixesCompatibility
    {
        private sealed class MenuState
        {
            internal ItemUiContext Context;
            internal InsuranceCompany Insurance;
            internal bool InsuranceMenu;
        }

        private sealed class WalletScope
        {
            internal object Menu;
            internal FieldInfo Field;
            internal object Previous;
        }

        private static readonly ConditionalWeakTable<object, MenuState> Menus = new ConditionalWeakTable<object, MenuState>();
        private static readonly Regex MoneyCaption = new Regex(@"\(([\d,\s]+)\s*₽\)", RegexOptions.CultureInvariant);
        private static readonly FieldInfo Controller = typeof(ItemUiContext).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(InventoryController));
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static bool subscribed;
        private static bool pending = true;
        internal static void Enable()
        {
            if (!subscribed)
            {
                subscribed = true;
                AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
                {
                    if (args.LoadedAssembly.GetName().Name.StartsWith("Tyfon.UIFixes", StringComparison.Ordinal))
                        pending = true;
                };
            }

            pending = false;
            var harmony = new Harmony("com.samc137.aishi.uifixes-currency");
            foreach (var name in new[]
            {
                "UIFixes.RepairInteractions",
                "UIFixes.InsuranceInteractions"
            }

            )
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false)).FirstOrDefault(found => found != null);
                if (type == null)
                    continue;
                foreach (var constructor in type.GetConstructors())
                    Patch(harmony, constructor, nameof(CaptureMenu));
                Patch(harmony, AccessTools.Method(type.BaseType, "method_2"), nameof(LabelCurrency));
                var validation = AccessTools.Method(type, "GetButtonInteraction");
                if (name.EndsWith("InsuranceInteractions", StringComparison.Ordinal))
                {
                    Patch(harmony, validation, nameof(ValidateInsurance));
                    Patch(harmony, AccessTools.Method(type, "LoadAsync"), nameof(InsuranceEligibility), nameof(RestoreInsurer));
                }
                else
                    Patch(harmony, validation, nameof(RepairWallet), nameof(RestoreWallet));
            }
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix, string finalizer = null)
        {
            if (target == null)
                throw new MissingMethodException("Aishi: UIFixes menu shape changed.");
            if (Patched.Contains(target))
                return;
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(AishiUiFixesCompatibility), prefix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(AishiUiFixesCompatibility), finalizer));
            Patched.Add(target);
        }

        internal static void Update()
        {
            if (!pending)
                return;
            try
            {
                Enable();
            }
            catch (Exception error)
            {
                AishiKeysPro.AishiKeysMod.Logger.LogError("Aishi UIFixes compatibility initialization failed: " + error);
            }
        }

        private static void CaptureMenu(object __instance, object[] __args)
        {
            var context = __args.OfType<ItemUiContext>().FirstOrDefault();
            if (context == null)
                return;
            Menus.GetValue(__instance, _ => new MenuState { Context = context, Insurance = context.Session?.InsuranceCompany, InsuranceMenu = __instance.GetType().Name == "InsuranceInteractions" });
        }

        internal static int DollarWallet(ItemUiContext context)
        {
            var inventory = Controller?.GetValue(context) as InventoryController;
            var stash = inventory?.Inventory?.Stash;
            if (stash == null)
                return 0;
            var dollars = stash.GetAllItems().Where(item => item.TemplateId == "5696686a4bdc2da3298b456a").Sum(item => (long)Math.Max(0, item.StackObjectsCount));
            return (int)Math.Min(int.MaxValue, dollars);
        }

        internal static string DollarCaption(string text, int wallet)
        {
            var match = MoneyCaption.Match(text ?? "");
            if (!match.Success || !int.TryParse(match.Groups[1].Value.Replace(",", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                return text;
            var color = amount > wallet ? "#FF0000" : "#ADB8BC";
            return text.Replace("#FF0000", color).Replace("#ADB8BC", color).Replace(match.Value, "($ " + amount.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static void LabelCurrency(object __instance, string __0, ref string __1)
        {
            if (!Menus.TryGetValue(__instance, out var state) || !__0.EndsWith(":" + AishiDollarUi.TraderId, StringComparison.Ordinal))
                return;
            __1 = DollarCaption(__1, DollarWallet(state.Context));
        }

        private static IEnumerable<InsuredItem> Insured(object menu)
        {
            return AccessTools.Field(menu.GetType(), "_insurableItems")?.GetValue(menu) as IEnumerable<InsuredItem> ?? Enumerable.Empty<InsuredItem>();
        }

        private static bool ValidateInsurance(object __instance, string __0, ref IResult __result)
        {
            if (!Menus.TryGetValue(__instance, out var state))
                return true;
            var trader = __0.Substring(__0.IndexOf(':') + 1);
            if (trader != AishiDollarUi.TraderId)
            {
                if (!Insured(__instance).Any(i => AishiInsuranceMenu.Special(i.Item)))
                    return true;
                __result = new FailedResult(InsuranceCompany.EInsuranceError.InvalidItem.ToString(), 0);
                return false;
            }

            var prices = AccessTools.Field(__instance.GetType(), "_prices")?.GetValue(__instance) as IDictionary;
            if (prices == null || !prices.Contains(trader))
                return true;
            __result = Convert.ToInt32(prices[trader]) > DollarWallet(state.Context) ? (IResult)new FailedResult("ragfair/Not enough money", 0) : SuccessfulResult.New;
            return false;
        }

        private static void RepairWallet(object __instance, string __0, out WalletScope __state)
        {
            __state = null;
            if (!Menus.TryGetValue(__instance, out var state) || !__0.EndsWith(":" + AishiDollarUi.TraderId, StringComparison.Ordinal))
                return;
            var field = AccessTools.Field(__instance.GetType(), "_playerRubles");
            if (field == null)
                return;
            __state = new WalletScope
            {
                Menu = __instance,
                Field = field,
                Previous = field.GetValue(__instance)
            };
            field.SetValue(__instance, DollarWallet(state.Context));
        }

        private static Exception RestoreWallet(Exception __exception, WalletScope __state)
        {
            __state?.Field.SetValue(__state.Menu, __state.Previous);
            return __exception;
        }

        private static void InsuranceEligibility(object __instance, out string __state)
        {
            __state = null;
            if (!Menus.TryGetValue(__instance, out var state) || state.Insurance == null || !AishiInsuranceMenu.HasAishi(state.Insurance))
                return;
            __state = state.Insurance.SelectedInsurerId;
            state.Insurance.SelectedInsurerId = AishiDollarUi.TraderId;
        }

        private static Exception RestoreInsurer(object __instance, string __state, Exception __exception)
        {
            if (__state != null && Menus.TryGetValue(__instance, out var state) && state.Insurance.SelectedInsurerId == AishiDollarUi.TraderId)
                state.Insurance.SelectedInsurerId = __state;
            return __exception;
        }
    }
}
