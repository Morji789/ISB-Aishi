using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Fika.Core.Main.Players;
using HarmonyLib;
using System;
using System.Reflection;

namespace AishiKeys.Fika
{
    [HarmonyPatch]
    internal static class FikaWorldInteractionKeyPatch
    {
        private sealed class DoorKeyState
        {
            internal WorldInteractiveObject Door;
            internal string OriginalKeyId;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            Type packet = typeof(FikaPlayer).Assembly.GetType("Fika.Core.Networking.Packets.Player.Common.SubPackets.WorldInteractionPacket", true);
            return AccessTools.Method(packet, "Execute", new[] { typeof(FikaPlayer) }) ?? throw new MissingMethodException(packet.FullName, "Execute");
        }

        [HarmonyPrefix]
        private static void Prefix(FikaPlayer player, EInteractionType ___InteractionType, EInteractionStage ___InteractionStage, string ___ItemId, string ___InteractiveId, out DoorKeyState __state)
        {
            __state = null;
            if (player == null || ___InteractionType != EInteractionType.Unlock || ___InteractionStage != EInteractionStage.Start || string.IsNullOrEmpty(___ItemId) || string.IsNullOrEmpty(___InteractiveId))
                return;
            Item keyItem = null;
            if (player.Profile != null && player.Profile.Inventory != null)
            {
                foreach (Item item in player.Profile.Inventory.GetPlayerItems((EPlayerItems)63))
                {
                    if (item != null && string.Equals(item.Id, ___ItemId, StringComparison.Ordinal))
                    {
                        keyItem = item;
                        break;
                    }
                }
            }

            if (keyItem == null || !AishiKeysMod.IsApprovedMasterKeyTemplate(keyItem.TemplateId))
                return;
            GameWorld world = Singleton<GameWorld>.Instance;
            WorldInteractiveObject door = world != null ? world.FindDoor(___InteractiveId) : null;
            if (door == null)
                return;
            __state = new DoorKeyState
            {
                Door = door,
                OriginalKeyId = door.KeyId
            };
            door.KeyId = keyItem.TemplateId;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DoorKeyState __state)
        {
            if (__state != null && __state.Door != null)
                __state.Door.KeyId = __state.OriginalKeyId;
            return __exception;
        }
    }
}
