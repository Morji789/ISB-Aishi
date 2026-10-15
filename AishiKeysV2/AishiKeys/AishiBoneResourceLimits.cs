using System;
using EFT.InventoryLogic;
using HarmonyLib;
#if SPT41
using Factory = EFT.ItemFactory;

#else
using Factory = ItemFactoryClass;
#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(Factory), "CreateItem")]
    internal static class AishiBoneResourceLimits
    {
        private const string Template = "282e86b6bc2955c6998fea9f";
        private static void Postfix(Item __result)
        {
            if (__result == null || __result.TemplateId != Template)
                return;
            var med = __result.GetItemComponent<MedKitComponent>();
            if (med == null || float.IsNaN(med.HpResource) || float.IsInfinity(med.HpResource))
                return;
            med.HpResource = Math.Min(2f, Math.Max(0f, med.HpResource));
        }
    }
}
