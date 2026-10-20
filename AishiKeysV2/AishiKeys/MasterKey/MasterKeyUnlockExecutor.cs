using System;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;

namespace AishiKeys.MasterKey
{
    internal static class MasterKeyUnlockExecutor
    {
        internal static bool UnlockRegularLocal(WorldInteractiveObject door, string masterKeyTemplateId)
        {
            if (!MasterKeyHelper.HasAssignedKey(door) || string.IsNullOrEmpty(masterKeyTemplateId))
                return false;
            string originalKeyId = door.KeyId;
            try
            {
                door.KeyId = masterKeyTemplateId;
                door.Interact((EInteractionType)2);
                return true;
            }
            finally
            {
                door.KeyId = originalKeyId;
            }
        }

        internal static bool UnlockRegularSynchronized(WorldInteractiveObject door, Player player, KeyComponent keyComponent)
        {
            return UnlockWithNativeKeyOperation(door, player, keyComponent);
        }

        internal static bool UnlockKeycard(KeycardDoor door, Player player, KeyComponent keyComponent)
        {
            return UnlockWithNativeKeyOperation(door, player, keyComponent);
        }

        private static bool UnlockWithNativeKeyOperation(WorldInteractiveObject door, Player player, KeyComponent keyComponent)
        {
            if (!MasterKeyHelper.HasAssignedKey(door))
                return false;
            if (keyComponent == null)
            {
                door.Interact((EInteractionType)2);
                return true;
            }

            if (player == null || keyComponent.Item == null)
                return false;
            player.MovementContext.ResetCanUsePropState();
            string originalKeyId = door.KeyId;
            GStruct156<KeyInteractionResultClass> unlockResult;
            try
            {
                door.KeyId = keyComponent.Item.TemplateId;
                unlockResult = door.UnlockOperation(keyComponent, player, door);
            }
            finally
            {
                door.KeyId = originalKeyId;
            }

            if (unlockResult.Failed)
                return false;
            unlockResult.Value.RaiseEvents(player.InventoryController, (CommandStatus)0);
            player.vmethod_0(door, unlockResult.Value, () => unlockResult.Value.RaiseEvents(player.InventoryController, (CommandStatus)1));
            return true;
        }
    }
}
