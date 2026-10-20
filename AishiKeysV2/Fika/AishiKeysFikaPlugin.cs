using AishiKeysPro;
using BepInEx;
using HarmonyLib;
using System;

namespace AishiKeys.Fika
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(AishiKeysMod.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(FikaPluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class AishiKeysFikaPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.samc137.aishikeysmod.fika";
        public const string PluginName = "Aishi Keys - Fika";
        public const string PluginVersion = "1.0.6";
        public const string FikaPluginGuid = "com.fika.core";
        private FikaAishiKeysNetworkBridge _bridge;
        private Harmony _harmony;
        public void Awake()
        {
            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(AishiKeysFikaPlugin).Assembly);
                _bridge = new FikaAishiKeysNetworkBridge(Logger);
                _bridge.Initialize();
                if (!AishiKeysMod.InstallNetworkBridge(_bridge))
                {
                    Logger.LogError("Aishi Keys Fika: the main plugin rejected the network bridge.");
                    _bridge.Dispose();
                    _bridge = null;
                    _harmony?.UnpatchSelf();
                    _harmony = null;
                    return;
                }

                Logger.LogInfo("Aishi Keys Fika 1.0.0 initialized. Host, client and headless synchronization enabled; " + "native Fika key interactions are patched for ultra-key compatibility.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Aishi Keys Fika initialization failed: " + ex);
                _bridge?.Dispose();
                _bridge = null;
                _harmony?.UnpatchSelf();
                _harmony = null;
            }
        }

        public void OnDestroy()
        {
            if (_bridge != null)
            {
                AishiKeysMod.RemoveNetworkBridge(_bridge);
                _bridge.Dispose();
                _bridge = null;
            }

            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
