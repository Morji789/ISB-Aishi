using EFT.InventoryLogic;

namespace AishiKeys.MasterKey
{
    internal static class MasterKeyItemResolver
    {
        internal static KeyComponent FindKeyComponent(Item item) => item?.GetItemComponent<KeyComponent>();
    }
}
