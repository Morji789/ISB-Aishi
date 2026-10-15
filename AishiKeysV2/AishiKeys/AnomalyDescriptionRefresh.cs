using System;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace AishiKeys
{
    [HarmonyPatch(typeof(ItemSpecificationPanel), "Update")]
    internal static class AnomalyDescriptionRefresh
    {
        private sealed class State
        {
            public State()
            {
            }

            internal Item Item;
            internal bool Revealed;
            internal bool Initialized;
            internal float Next;
        }

#if SPT41
        private const string ExaminedProperty = "Examined";
#else
        private const string ExaminedProperty = "Boolean_0";
#endif
        private static readonly Func<ItemSpecificationPanel, bool> Examined = (Func<ItemSpecificationPanel, bool>)Delegate.CreateDelegate(typeof(Func<ItemSpecificationPanel, bool>), AccessTools.PropertyGetter(typeof(ItemSpecificationPanel), ExaminedProperty));
        private static readonly ConditionalWeakTable<ItemSpecificationPanel, State> States = new ConditionalWeakTable<ItemSpecificationPanel, State>();
#if SPT41
        private static void Postfix(ItemSpecificationPanel __instance, Item ____item, ItemInfoWindowLabels ____itemLabels)
        {
            var item = ____item;
#else
        private static void Postfix(ItemSpecificationPanel __instance, Item ___item_0, ItemInfoWindowLabels ____itemLabels)
        {
            var item = ___item_0;
#endif
            if (item == null || !AishiInventoryDescriptions.IsAnomaly(item.TemplateId) || ____itemLabels == null || !Examined(__instance))
                return;
            var state = States.GetOrCreateValue(__instance);
            if (ReferenceEquals(item, state.Item) && Time.unscaledTime < state.Next)
                return;
            state.Next = Time.unscaledTime + .2f;
            bool revealed = AnomalyRevealClient.IsRevealed(item.TemplateId);
            if (state.Initialized && ReferenceEquals(item, state.Item) && state.Revealed == revealed)
                return;
            state.Initialized = true;
            state.Item = item;
            state.Revealed = revealed;
#if SPT41
            ____itemLabels.SetDescriptionText(item.Description.Localized());
#else
            ____itemLabels.method_2(item.Description.Localized());
#endif
        }
    }
}
