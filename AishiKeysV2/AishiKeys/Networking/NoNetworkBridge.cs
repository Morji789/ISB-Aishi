using System;

namespace AishiKeys.Networking
{
    internal sealed class NoNetworkBridge : IAishiKeysNetworkBridge
    {
        internal static readonly NoNetworkBridge Instance = new NoNetworkBridge();
        private NoNetworkBridge()
        {
        }

        public bool Active => false;
        public bool Ready => false;
        public bool IsServer => true;
        public bool IsHeadless => false;

        public bool Broadcast(string payload)
        {
            return false;
        }

        public bool SendToServer(string payload)
        {
            return false;
        }

        public void DrainReceivedPayloads(Action<string> handler)
        {
        }

        public void Dispose()
        {
        }
    }
}
