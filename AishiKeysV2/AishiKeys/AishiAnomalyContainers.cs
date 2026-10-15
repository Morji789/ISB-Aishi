using System.Collections.Generic;
using EFT.InventoryLogic;
using HarmonyLib;
#if SPT41
using ContainerFilters = EFT.InventoryLogic.ItemFilterExtension;
#else
using ContainerFilters = GClass3124;

#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(ContainerFilters), "CanAccept")]
    internal static class AishiAnomalyContainers
    {
        private static readonly HashSet<string> BeltIds = new HashSet<string>
        {
            "6761b213607f9a6f79017aef",
            "6761b213607f9a6f79017af1",
            "6761b213607f9a6f79017af2",
            "6761b213607f9a6f79017af3",
            "6761b213607f9a6f79017af4",
            "6761b213607f9a6f79017af5",
            "6761b213607f9a6f79017af6",
            "6761b213607f9a6f79017af7",
            "6761b213607f9a6f79017af9",
            "6761b213607f9a6f79017afa",
            "6761b213607f9a6f79017afb",
            "6761b213607f9a6f79017afc",
            "6761b213607f9a6f79017afd",
            "6761b213607f9a6f79017afe",
            "6761b213607f9a6f79017aff",
            "6807e54dc5a45c5443154a29",
            "68084891d93e159932d7e326",
            "680e8d81e16a7caa3dd10730",
            "68170b2cb595bf820b60571a",
            "69352d5c409abe93d29321e4",
            "681baec29eb1732a417bd7dd",
            "681bb00c7fecf6d490b2fb3c",
            "681bcd300480896883303f71",
            "69364a368a4b47eeaf9ba4a3"
        };
        private static bool IsBelt(Item item) => item != null && (BeltIds.Contains(item.TemplateId) || item.GetType().Name == "CustomBeltItemClass");
        private static void Postfix(EFT.InventoryLogic.IContainer __0, Item __1, ref bool __result)
        {
            if (!__result || __0 == null || __1 == null)
                return;
            bool restricted = false;
            var container = __0;
            for (int i = 0; i < 64 && container != null; i++)
            {
                if (container.ID == "SecuredContainer" || container.ID.StartsWith("SpecialSlot"))
                {
                    restricted = true;
                    break;
                }

                var parent = container.ParentItem;
                if (IsBelt(parent) || parent?.Template?.ParentId == "5448bf274bdc2dfc2f8b456a")
                {
                    restricted = true;
                    break;
                }

                container = parent?.CurrentAddress?.Container;
            }

            if (!restricted)
                return;
            var pending = new Stack<Item>();
            var seen = new HashSet<Item>();
            pending.Push(__1);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (!seen.Add(item))
                    continue;
                if (AishiAnomalyContainment.IsCase(item) || AishiInventoryDescriptions.IsAnomaly(item.TemplateId))
                {
                    __result = false;
                    return;
                }

                if (!(item is CompoundItem compound))
                    continue;
                if (compound.Grids != null)
                    foreach (var grid in compound.Grids)
                        if (grid != null)
                            foreach (var child in grid.Items)
                                if (child != null)
                                    pending.Push(child);
                if (compound.Slots != null)
                    foreach (var slot in compound.Slots)
                        if (slot?.ContainedItem != null)
                            pending.Push(slot.ContainedItem);
            }
        }
    }
}
