using AishiKeys.MasterKey;
using AishiKeysPro;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;

namespace AishiKeys
{
    public class MasterKeyInteraction
    {
        private readonly WorldInteractiveObject _door;
        private readonly GamePlayerOwner _owner;
        private readonly KeyComponent _keyComponent;
        public MasterKeyInteraction(WorldInteractiveObject door, GamePlayerOwner owner, KeyComponent keyComponent)
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
            if (AishiKeysMod.TryHandleSynchronizedUnlock(_door, _owner, templateId, _keyComponent, false))
            {
                return;
            }

            if (_owner == null || _owner.Player == null)
                return;
            MasterKeyUnlockExecutor.UnlockRegularLocal(_door, templateId);
        }
    }
}
