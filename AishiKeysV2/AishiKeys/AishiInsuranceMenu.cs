using System;
using AishiShared;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Insurance;
using InsuranceCompany = InsuranceCompanyClass;
using HarmonyLib;

namespace AishiKeys
{
    [HarmonyPatch(typeof(ContextInteractionSwitcherClass), "IsInteractive")]
    internal static class AishiInsuranceMenu
    {
        internal static bool Special(Item item) => item != null && item.Template.InsuranceDisabled && IsbInsuranceCatalog.Ids.Contains(item.Template.StringId);
        internal static bool HasAishi(InsuranceCompany company) => company != null && company.Insurers.Any(trader => trader.Info.Id == AishiDollarUi.TraderId);
        internal static bool Available(InsuranceCompany company, Item item)
        {
            if (company.ItemTypeAvailableForInsurance(item))
                return true;
            return Special(item) && HasAishi(company) && (!(item.CurrentAddress is GClass3391 address) || !address.Slot.Locked);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int changed = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(InsuranceCompany) && method.Name == "ItemTypeAvailableForInsurance")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(AishiInsuranceMenu), nameof(Available));
                    changed++;
                }

                yield return instruction;
            }

            if (changed != 1)
                throw new InvalidOperationException("Aishi: native inventory insurance menu changed.");
        }
    }

    [HarmonyPatch(typeof(InsuranceWindow), "Show")]
    internal static class AishiInsuranceMenuSelection
    {
        private static void Prefix(ItemContextAbstractClass __0, InsuranceCompany __2)
        {
            if (AishiInsuranceMenu.Special(__0?.Item) && AishiInsuranceMenu.HasAishi(__2))
                __2.SelectedInsurerId = AishiDollarUi.TraderId;
        }
    }
}
