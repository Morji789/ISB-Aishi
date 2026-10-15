using EFT.InventoryLogic;
using HarmonyLib;

namespace AishiKeys
{
    [HarmonyPatch(typeof(StimulatorBuffsComponent), "SetupStimulatorBuffsAttributes")]
    internal static class AishiMysteryAttributes
    {
        internal static bool IsMystery(string id) => id == "b27508815b9307ccd1bffa36" || id == "1800c994e11cfa35b423e8ed" || id == "36be503998aadd53701c57e3" || id == "f131c988930a86c7e5610c57";
        private static void Prefix(Item __0, out int __state)
        {
            __state = __0?.Attributes?.Count ?? 0;
        }

        private static void Postfix(Item __0, int __state)
        {
            if (__0 == null || !IsMystery(__0.TemplateId))
                return;
            for (int i = __state; i < __0.Attributes.Count; ++i)
            {
                var attribute = __0.Attributes[i];
                attribute.Name = "???????";
                attribute.DisplayNameFunc = () => "???????";
                attribute.StringValue = () => "???????";
                attribute.FullStringValue = () => "???????";
            }
        }
    }
}
