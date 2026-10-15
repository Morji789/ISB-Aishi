using AishiKeysPro;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace AishiKeys.Fika
{
    public static class AishiKeysFikaBootstrap
    {
        private const string HarmonyId = "com.samc137.aishi";
        private static readonly object SyncRoot = new object ();
        private static FikaAishiKeysNetworkBridge _bridge;
        private static Harmony _harmony;
        public static void Initialize(ManualLogSource log)
        {
            lock (SyncRoot)
            {
                if (_bridge != null && _bridge.Active)
                    return;
                FikaAishiKeysNetworkBridge bridge = new FikaAishiKeysNetworkBridge(log);
                Harmony harmony = new Harmony(HarmonyId);
                bool bridgeInstalled = false;
                try
                {
                    harmony.PatchAll(typeof(AishiKeysFikaBootstrap).Assembly);
                    bridge.Initialize();
                    if (!AishiKeysMod.InstallNetworkBridge(bridge))
                    {
                        throw new InvalidOperationException("The main Aishi Keys plugin rejected the Fika network bridge.");
                    }

                    bridgeInstalled = true;
                    _bridge = bridge;
                    _harmony = harmony;
                    log?.LogInfo("Aishi Keys: optional Fika synchronization enabled for host, clients and headless; " + "native Fika key interactions are patched for ultra-key compatibility.");
                }
                catch
                {
                    if (bridgeInstalled)
                        AishiKeysMod.RemoveNetworkBridge(bridge);
                    bridge.Dispose();
                    harmony.UnpatchSelf();
                    throw;
                }
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                FikaAishiKeysNetworkBridge bridge = _bridge;
                Harmony harmony = _harmony;
                _bridge = null;
                _harmony = null;
                if (bridge != null)
                {
                    AishiKeysMod.RemoveNetworkBridge(bridge);
                    try
                    {
                        bridge.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }

                harmony?.UnpatchSelf();
            }
        }
    }
}
