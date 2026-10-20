using System;
using System.Collections.Generic;
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
    public class AishiKeycardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GetActionsClass).GetMethod("smethod_13", BindingFlags.Static | BindingFlags.Public);
        }

        [PatchPostfix]
        private static void Postfix(ref ActionsReturnClass __result, GamePlayerOwner owner, KeycardDoor door, bool isProxy)
        {
            if (door.DoorState != (EFT.Interactive.EDoorState)1)
                return;
            GameWorld instance = Singleton<GameWorld>.Instance;
            if (!((instance != null) ? instance.MainPlayer : null))
                return;
            if (!MasterKeyHelper.HasAssignedKey(door))
                return;
            if (WorldInteractionUtils.IsBotInteraction(owner))
                return;
            List<Item> masterKeys = (
                from item in owner.Player.Profile.Inventory.GetPlayerItems((EFT.InventoryLogic.EPlayerItems)63)
                where MasterKeyHelper.AllMasterKeyIds.Contains(item.TemplateId)select item).ToList<Item>();
            if (masterKeys.Count == 0)
                return;
            List<ValueTuple<Item, KeyComponent, string>> resolvedKeys = new List<ValueTuple<Item, KeyComponent, string>>();
            foreach (Item keyItem in masterKeys)
            {
                KeyComponent keyComponent = MasterKeyItemResolver.FindKeyComponent(keyItem);
                if (keyComponent == null)
                    continue;
                resolvedKeys.Add(new ValueTuple<Item, KeyComponent, string>(keyItem, keyComponent, GClass2348.Localized(keyItem.ShortName, null)));
            }

            if (resolvedKeys.Count == 0)
                return;
            foreach (ActionsTypesClass action in __result.Actions)
            {
                if (string.IsNullOrEmpty(action.Name))
                    continue;
                foreach (ValueTuple<Item, KeyComponent, string> resolved in resolvedKeys)
                {
                    KeyComponent keyComponent = resolved.Item2;
                    string shortName = resolved.Item3;
                    if (action.Name.Contains(shortName))
                    {
                        action.Action = new Action(new MasterKeyKeycardInteraction(door, owner, keyComponent).Unlock);
                        break;
                    }
                }
            }
        }
    }
}
