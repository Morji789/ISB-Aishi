using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
#if SPT41
using Factory = EFT.ItemFactory;
using Spec = EFT.InventoryLogic.SpecItem;
using SpecTemplate = EFT.InventoryLogic.SpecItemTemplate;
using Diff = UnparsedData;
using Deserializer = EFT.ItemDeserializer;
using NativeComponent = EFT.ComponentAttribute;
using ResourceTemplate = EFT.InventoryLogic.IResourceItemTemplate;

#else
using Factory = ItemFactoryClass;
using Spec = SpecItemItemClass;
using SpecTemplate = SpecItemTemplateClass;
using Diff = GClass846;
using Deserializer = GClass1911;
using NativeComponent = GAttribute26;
using ResourceTemplate = GInterface414;
#endif
namespace AishiKeys
{
    internal sealed class AishiAnalyserItem : Spec
    {
        internal const string TemplateIdValue = "2b20c487a99515b22b11ff78";
        private sealed class Charges : ResourceTemplate
        {
            public float MaxResource => 5f;
        }

        private static readonly Charges ChargeTemplate = new Charges();
        [NativeComponent]
        [JsonProperty("Resource")]
        public readonly ResourceComponent ResourceHolderComponent;
        internal AishiAnalyserItem(string id, SpecTemplate template) : base(id, template)
        {
            Components.Add(ResourceHolderComponent = new ResourceComponent(this, ChargeTemplate));
        }
    }

    internal sealed class AishiChargedAnomalyItem : Item
    {
        private sealed class Charges : ResourceTemplate
        {
            public float MaxResource { get; }

            internal Charges(float maximum)
            {
                MaxResource = maximum;
            }
        }

        [NativeComponent]
        [JsonProperty("Resource")]
        public readonly ResourceComponent ResourceHolderComponent;
        internal AishiChargedAnomalyItem(string id, ItemTemplate template, float maximum) : base(id, template)
        {
            Components.Add(ResourceHolderComponent = new ResourceComponent(this, new Charges(maximum)));
        }
    }

    [HarmonyPatch(typeof(Factory), "CreateItem")]
    internal static class AishiAnalyserResourceFactory
    {
        private static bool Prefix(Factory __instance, string __0, string __1, Diff __2, ref Item __result)
        {
            if (__1 != AishiAnalyserItem.TemplateIdValue && __1 != AishiAnomalyS2.Press && __1 != AishiAnomalyS2.Defibrillator)
                return true;
            if (!__instance.ItemTemplates.TryGetValue(__1, out var template))
                return true;
            Item item;
            if (__1 == AishiAnalyserItem.TemplateIdValue)
            {
                if (!(template is SpecTemplate special))
                    return true;
                item = new AishiAnalyserItem(__0, special);
            }
            else
                item = new AishiChargedAnomalyItem(__0, template, __1 == AishiAnomalyS2.Press ? 10f : 1f);
            __result = __2 == null ? item : Deserializer.CreateItem(item, __2);
            return false;
        }
    }
}
