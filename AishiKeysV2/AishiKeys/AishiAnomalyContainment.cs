using EFT.InventoryLogic;

namespace AishiKeys
{
    internal static class AishiAnomalyContainment
    {
        internal const string Template = "667a400d44455c10e40f761f";
        internal static bool IsCase(Item item) => item?.TemplateId == Template;
        internal static bool IsSuppressed(Item item)
        {
            var container = item?.CurrentAddress?.Container;
            for (int i = 0; i < 64 && container != null; i++)
            {
                if (container is IItemOwner)
                    return false;
                var parent = container.ParentItem;
                if (IsCase(parent))
                    return true;
                container = parent?.CurrentAddress?.Container;
            }

            return false;
        }
    }
}
