using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
#if SPT41
using ItemActions = EFT.InventoryLogic.ItemManipulator;

#else
using ItemActions = InteractionsHandlerClass;
#endif
namespace AishiKeys
{
    internal static class AishiAnomalyDuck
    {
        internal const string Template = "ae711ed0c5878f74a4264e27";
        private const string Request = "ADQ|", Result = "ADR|";
        private sealed class Change
        {
            internal string Profile, Item;
            internal float Remaining, RetryAt;
        }

        private static bool updating;
        private static readonly List<Change> waitingSnapshot = new List<Change>();
        private static readonly List<Change> pendingSnapshot = new List<Change>();
        private static GameWorld world;
        private static readonly Dictionary<string, Change> pending = new Dictionary<string, Change>();
        private static readonly Dictionary<string, Change> waiting = new Dictionary<string, Change>();
        private static readonly Dictionary<string, float> balances = new Dictionary<string, float>();
        private static AishiKeys.Networking.IAishiKeysNetworkBridge Network => AishiKeysMod.Instance?.NetworkBridge;

        private static bool CurrentWorld()
        {
            var live = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (!ReferenceEquals(world, live))
            {
                world = live;
                pending.Clear();
                waiting.Clear();
                balances.Clear();
            }

            return world != null && !(world is HideoutGameWorld);
        }

        internal static Item Active(AishiAnomalyInventory state)
        {
            Item selected = null;
            foreach (var item in state.CarriedItems)
                if (item.TemplateId == Template && item.GetItemComponent<ResourceComponent>()?.Value > 0f && (selected == null || string.CompareOrdinal(item.Id, selected.Id) < 0))
                    selected = item;
            return selected;
        }

        internal static void Inspect(AishiAnomalyInventory state)
        {
            foreach (var item in state.CarriedItems)
                if (item.TemplateId == Template && item.GetItemComponent<ResourceComponent>()?.Value <= 0f && !pending.ContainsKey(item.Id) && !waiting.ContainsKey(item.Id))
                    Changed(state.Owner, item, 0f);
        }

        internal static float Absorb(AishiAnomalyInventory state, float damage)
        {
            var item = Active(state);
            if (item == null || !(damage > 0f) || float.IsInfinity(damage))
                return damage;
            var resource = item.GetItemComponent<ResourceComponent>();
            float absorbed = Mathf.Min(damage * .5f, resource.Value);
            resource.Value = Mathf.Max(0f, resource.Value - absorbed);
            item.RaiseRefreshEvent(false, true);
            Changed(state.Owner, item, resource.Value);
            return damage - absorbed;
        }

        internal static void Changed(Player owner, Item item, float remaining)
        {
            if (!CurrentWorld() || owner == null || !Accepts(item))
                return;
            var change = new Change
            {
                Profile = owner.ProfileId,
                Item = item.Id,
                Remaining = remaining
            };
            if (remaining <= 0f)
            {
                balances[change.Profile + ":" + change.Item] = 0f;
                if (!Consume(owner, item))
                    waiting[item.Id] = change;
            }

            if (Network?.Active == true)
            {
                pending[item.Id] = change;
                Update();
            }
        }

        internal static void Update()
        {
            if (updating || !CurrentWorld())
                return;
            updating = true;
            try
            {
                waitingSnapshot.AddRange(waiting.Values);
                foreach (var change in waitingSnapshot)
                    if (Apply(change))
                        waiting.Remove(change.Item);
                var network = Network;
                if (network?.Ready != true)
                    return;
                pendingSnapshot.AddRange(pending.Values);
                foreach (var change in pendingSnapshot)
                {
                    if (Time.realtimeSinceStartup < change.RetryAt)
                        continue;
                    change.RetryAt = Time.realtimeSinceStartup + 1f;
                    string payload = Encode(Request, change);
                    if (network.IsServer)
                        Handle(payload);
                    else
                        network.SendToServer(payload);
                }
            }
            finally
            {
                waitingSnapshot.Clear();
                pendingSnapshot.Clear();
                updating = false;
            }
        }

        private static string Encode(string prefix, Change c) => prefix + c.Profile + "|" + c.Item + "|" + c.Remaining.ToString("R", CultureInfo.InvariantCulture);
        internal static bool Handle(string payload)
        {
            if (payload == null || (!payload.StartsWith(Request, StringComparison.Ordinal) && !payload.StartsWith(Result, StringComparison.Ordinal)))
                return false;
            var network = Network;
            if (!CurrentWorld() || network?.Ready != true || payload.Length > 256)
                return true;
            var parts = payload.Split('|');
            if (parts.Length != 4 || parts[1].Length == 0 || parts[1].Length > 64 || parts[2].Length != 24 || !parts[2].All(Uri.IsHexDigit) || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || value < 0f || value > 500f)
                return true;
            var change = new Change
            {
                Profile = parts[1],
                Item = parts[2],
                Remaining = value
            };
            string key = change.Profile + ":" + change.Item;
            if (payload.StartsWith(Request, StringComparison.Ordinal))
            {
                if (!network.IsServer)
                    return true;
                var owner = Find(change.Profile);
                var item = FindItem(owner, change.Item);
                if (item == null && !balances.ContainsKey(key))
                    return true;
                if (item != null && (owner.IsAI || !Accepts(item) || item.GetItemComponent<ResourceComponent>() == null))
                    return true;
                float current = item?.GetItemComponent<ResourceComponent>()?.Value ?? balances[key];
                if (balances.TryGetValue(key, out float previous))
                    current = Mathf.Min(current, previous);
                change.Remaining = Mathf.Min(current, value);
                balances[key] = change.Remaining;
                if (!Apply(change))
                    waiting[change.Item] = change;
                Confirm(change);
                network.Broadcast(Encode(Result, change));
            }
            else if (!network.IsServer)
            {
                if (balances.TryGetValue(key, out float previous))
                    change.Remaining = Mathf.Min(previous, value);
                balances[key] = change.Remaining;
                if (!Apply(change))
                    waiting[change.Item] = change;
                Confirm(change);
            }

            return true;
        }

        private static void Confirm(Change change)
        {
            if (pending.TryGetValue(change.Item, out var local) && local.Profile == change.Profile && change.Remaining <= local.Remaining)
                pending.Remove(change.Item);
        }

        private static Player Find(string profile)
        {
            if (world == null)
                return null;
            foreach (var registered in world.RegisteredPlayers)
                if (registered is Player player && player.ProfileId == profile)
                    return player;
            return null;
        }

        private static Item FindItem(Player owner, string id)
        {
            var equipment = owner?.InventoryController?.Inventory?.Equipment;
            if (equipment == null)
                return null;
            foreach (var item in equipment.GetAllItems())
                if (item.Id == id)
                    return item;
            return null;
        }

        private static bool Apply(Change change)
        {
            var owner = Find(change.Profile);
            if (owner == null)
                return false;
            var item = FindItem(owner, change.Item);
            if (item == null)
                return true;
            if (!Accepts(item))
                return true;
            var resource = item.GetItemComponent<ResourceComponent>();
            if (resource == null)
                return true;
            resource.Value = Mathf.Min(resource.Value, change.Remaining);
            item.RaiseRefreshEvent(false, true);
            return resource.Value > 0f || Consume(owner, item);
        }

        internal static bool Accepts(Item item) => item != null && (item.TemplateId == Template || item.TemplateId == AishiAnomalyS2.Press || item.TemplateId == AishiAnomalyS2.Defibrillator);
        internal static void Reconcile(Player owner, Item item, float remaining)
        {
            if (!CurrentWorld() || owner == null || !Accepts(item))
                return;
            var change = new Change
            {
                Profile = owner.ProfileId,
                Item = item.Id,
                Remaining = remaining
            };
            string key = change.Profile + ":" + change.Item;
            if (balances.TryGetValue(key, out float previous))
                change.Remaining = Mathf.Min(previous, remaining);
            balances[key] = change.Remaining;
            if (!Apply(change))
                waiting[change.Item] = change;
            Confirm(change);
        }

        private static bool Consume(Player owner, Item item)
        {
            if (item.CurrentAddress == null)
                return true;
            var removed = ItemActions.RemoveWithoutRestrictions(item, owner.InventoryController);
            if (removed.Failed)
                return false;
            removed.Value.RaiseEvents(owner.InventoryController, CommandStatus.Succeed);
            return true;
        }
    }
}
