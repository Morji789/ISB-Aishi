using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AishiShared;
using EFT.InventoryLogic;
using HarmonyLib;
using InsuranceCompany = InsuranceCompanyClass;
using InsuredItem = ItemClass;

namespace AishiKeys
{
    [HarmonyPatch(typeof(InsuranceCompany), "method_0")]
    internal static class AishiInsuranceReceipt
    {
        public static bool ReceiptAvailable(InsuranceCompany company, InsuredItem insured)
        {
            var item = insured.Item;
            if (insured.InsurerId == AishiDollarUi.TraderId && item.Template.InsuranceDisabled && IsbInsuranceCatalog.Ids.Contains(item.Template.StringId))
                return !(item.CurrentAddress is GClass3391 address) || !address.Slot.Locked;
            return company.ItemTypeAvailableForInsurance(insured);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int changed = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(InsuranceCompany) && method.Name == "ItemTypeAvailableForInsurance")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(AishiInsuranceReceipt), nameof(ReceiptAvailable));
                    changed++;
                }

                yield return instruction;
            }

            if (changed != 1)
                throw new InvalidOperationException("Aishi: native insurance registration changed.");
        }
    }
}
