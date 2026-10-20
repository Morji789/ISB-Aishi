using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AishiKeys
{
    public static class WorldInteractionUtils
    {
        private static readonly string[] StableIdMemberNames =
        {
            "Id",
            "InteractiveId",
            "ObjectId",
            "DoorId",
            "NetId"
        };
        private static readonly Dictionary<Type, MemberInfo[]> StableMembers = new Dictionary<Type, MemberInfo[]>();
        private static readonly Dictionary<string, WeakReference> ResolvedDoors = new Dictionary<string, WeakReference>(StringComparer.Ordinal);
        private static float nextDoorScan;
        static WorldInteractionUtils()
        {
            SceneManager.sceneLoaded += ClearResolvedDoors;
            SceneManager.sceneUnloaded += ClearResolvedDoors;
        }

        private static void ClearResolvedDoors(Scene scene, LoadSceneMode mode) => ClearResolvedDoors(scene);
        private static void ClearResolvedDoors(Scene scene)
        {
            MasterKey.MasterKeyHelper.ResetDoorAssignments();
            ResolvedDoors.Clear();
            nextDoorScan = 0f;
        }

        private static MemberInfo[] GetStableMembers(Type type)
        {
            if (StableMembers.TryGetValue(type, out var cached))
                return cached;
            var members = new List<MemberInfo>();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in StableIdMemberNames)
            {
                var property = FindProperty(type, name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    members.Add(property);
                var field = FindField(type, name, flags);
                if (field != null)
                    members.Add(field);
            }

            return StableMembers[type] = members.ToArray();
        }

        public static bool IsBotInteraction(GamePlayerOwner owner)
        {
            int? ownerId = null;
            if (owner != null)
            {
                Player player = owner.Player;
                ownerId = player != null ? new int? (player.Id) : null;
            }

            int? mainPlayerId = null;
            GameWorld instance = Singleton<GameWorld>.Instance;
            if (instance != null)
            {
                Player mainPlayer = instance.MainPlayer;
                mainPlayerId = mainPlayer != null ? new int? (mainPlayer.Id) : null;
            }

            return !(ownerId.GetValueOrDefault() == mainPlayerId.GetValueOrDefault() && (ownerId != null) == (mainPlayerId != null));
        }

        internal static string GetStableInteractiveId(WorldInteractiveObject interactive)
        {
            if (interactive == null)
                return string.Empty;
            foreach (var member in GetStableMembers(interactive.GetType()))
            {
                try
                {
                    var value = member is PropertyInfo property ? property.GetValue(interactive, null) : ((FieldInfo)member).GetValue(interactive);
                    var text = ConvertStableValue(value);
                    if (!string.IsNullOrEmpty(text))
                        return member.Name + ":" + text;
                }
                catch
                {
                }
            }

            Transform transform = interactive.transform;
            if (transform == null)
                return string.Empty;
            Scene scene = interactive.gameObject.scene;
            Vector3 position = transform.position;
            return "Path:" + (scene.IsValid() ? scene.name : string.Empty) + "|" + BuildHierarchyPath(transform) + "|" + position.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + position.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + position.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static WorldInteractiveObject FindInteractiveByStableId(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
                return null;
            if (ResolvedDoors.TryGetValue(stableId, out var reference))
            {
                var cached = reference.Target as WorldInteractiveObject;
                if (cached != null && cached.gameObject.scene.IsValid() && cached.gameObject.scene.isLoaded && string.Equals(GetStableInteractiveId(cached), stableId, StringComparison.Ordinal))
                    return cached;
                ResolvedDoors.Remove(stableId);
            }

            if (Time.realtimeSinceStartup < nextDoorScan)
                return null;
            nextDoorScan = Time.realtimeSinceStartup + 1f;
            WorldInteractiveObject match = null;
            foreach (var interactive in Resources.FindObjectsOfTypeAll<WorldInteractiveObject>())
            {
                if (interactive == null || interactive.gameObject == null)
                    continue;
                var scene = interactive.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                string id = GetStableInteractiveId(interactive);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!ResolvedDoors.ContainsKey(id))
                    ResolvedDoors[id] = new WeakReference(interactive);
                if (match == null && string.Equals(id, stableId, StringComparison.Ordinal))
                    match = interactive;
            }

            if (match != null)
                ResolvedDoors[stableId] = new WeakReference(match);
            return match;
        }

        internal static bool TryApplyAuthoritativeDoorState(WorldInteractiveObject door, int authorityState)
        {
            if (door == null || !MasterKey.MasterKeyHelper.HasAssignedKey(door))
                return false;
            EDoorState targetState = ResolveFallbackDoorState(authorityState);
            if (door.DoorState == targetState)
                return true;
            if (door.DoorState == (EDoorState)1 && targetState != (EDoorState)1)
            {
                try
                {
                    door.Interact((EInteractionType)2);
                }
                catch
                {
                }

                if (door.DoorState != (EDoorState)1 && (authorityState < 0 || door.DoorState == targetState))
                {
                    return true;
                }
            }

            Type type = door.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = FindProperty(type, "DoorState", flags);
            if (property != null && property.PropertyType == typeof(EDoorState))
            {
                MethodInfo setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    try
                    {
                        setter.Invoke(door, new object[] { targetState });
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(flags | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType != typeof(EDoorState))
                        continue;
                    if (field.Name.IndexOf("doorstate", StringComparison.OrdinalIgnoreCase) < 0 && field.Name.IndexOf("doorState", StringComparison.OrdinalIgnoreCase) < 0 && field.Name.IndexOf("<DoorState>", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    try
                    {
                        field.SetValue(door, targetState);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static EDoorState ResolveFallbackDoorState(int authorityState)
        {
            if (authorityState >= byte.MinValue && authorityState <= byte.MaxValue && authorityState != 1)
            {
                return (EDoorState)(byte)authorityState;
            }

            string[] preferredNames =
            {
                "Shut",
                "Closed",
                "Open"
            };
            foreach (string name in preferredNames)
            {
                EDoorState parsed;
                if (Enum.TryParse(name, true, out parsed) && (int)parsed != 1)
                    return parsed;
            }

            Array values = Enum.GetValues(typeof(EDoorState));
            foreach (object value in values)
            {
                EDoorState state = (EDoorState)value;
                if ((int)state != 1)
                    return state;
            }

            return (EDoorState)0;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            StringBuilder builder = new StringBuilder();
            Transform current = transform;
            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Insert(0, '/');
                builder.Insert(0, current.name ?? string.Empty);
                current = current.parent;
            }

            return builder.ToString();
        }

        private static string ConvertStableValue(object value)
        {
            if (value == null)
                return string.Empty;
            string text = value as string;
            if (text != null)
                return text.Trim();
            if (value is Guid)
                return value.ToString();
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return string.Empty;
        }

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }
    }
}
