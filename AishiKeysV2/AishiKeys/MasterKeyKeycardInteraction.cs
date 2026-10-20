using AishiKeys.MasterKey;
using AishiKeysPro;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;

namespace AishiKeys
{
    public class MasterKeyKeycardInteraction
    {
        private readonly KeycardDoor _door;
        private readonly GamePlayerOwner _owner;
        private readonly KeyComponent _keyComponent;
        public MasterKeyKeycardInteraction(KeycardDoor door, GamePlayerOwner owner, KeyComponent keyComponent)
        {
            _door = door;
            _owner = owner;
            _keyComponent = keyComponent;
        }

        public void Unlock()
        {
            if (!MasterKeyHelper.HasAssignedKey(_door))
                return;
            string templateId = _keyComponent != null && _keyComponent.Item != null ? _keyComponent.Item.TemplateId : string.Empty;
            if (AishiKeysMod.TryHandleSynchronizedUnlock(_door, _owner, templateId, _keyComponent, true))
            {
                return;
            }

            if (_owner == null || _owner.Player == null)
                return;
            MasterKeyUnlockExecutor.UnlockKeycard(_door, _owner.Player, _keyComponent);
        }
    }
}
