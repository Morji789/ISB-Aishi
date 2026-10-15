using System;
using System.Linq;
using System.Reflection;
using AishiKeys.MasterKey;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace AishiKeys
{
    public class AishiKeysPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GetActionsClass).GetMethod("smethod_5", BindingFlags.Static | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void Postfix(ref ActionsReturnClass __result, GamePlayerOwner owner, WorldInteractiveObject worldInteractiveObject)
        {
            if (worldInteractiveObject == null || worldInteractiveObject.DoorState != (EDoorState)1)
            {
                return;
            }

            if (worldInteractiveObject is KeycardDoor)
                return;
            GameWorld instance = Singleton<GameWorld>.Instance;
            if (instance == null || instance.MainPlayer == null)
                return;
            if (string.IsNullOrEmpty(worldInteractiveObject.KeyId))
                return;
            if (WorldInteractionUtils.IsBotInteraction(owner) || owner == null || owner.Player == null || owner.Player.Profile == null || owner.Player.Profile.Inventory == null)
            {
                return;
            }

            string masterKeyTemplateId = MasterKeyHelper.GetMasterKey(Config.UltraKeys.AishiMK);
            Item masterKey = owner.Player.Profile.Inventory.GetPlayerItems((EPlayerItems)63).FirstOrDefault(item => item != null && string.Equals(item.TemplateId, masterKeyTemplateId, StringComparison.Ordinal));
            if (masterKey == null)
                return;
            KeyComponent keyComponent = MasterKeyItemResolver.FindKeyComponent(masterKey);
            if (keyComponent == null)
            {
                AishiKeysPro.AishiKeysMod.Logger?.LogWarning("Aishi Keys could not expose the master-key action because " + "the configured Aishi key item has no KeyComponent.");
                return;
            }

            ActionsTypesClass action = new ActionsTypesClass
            {
                Name = GClass2348.Localized("Try", null) + " " + GClass2348.Localized(masterKey.ShortName, null),
                Disabled = !worldInteractiveObject.Operatable
            };
            action.Action = new Action(new MasterKeyInteraction(worldInteractiveObject, owner, keyComponent).Unlock);
            __result.Actions.Add(action);
        }
    }
}
