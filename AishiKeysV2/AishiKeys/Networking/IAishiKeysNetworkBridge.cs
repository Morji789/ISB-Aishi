using System;

namespace AishiKeys.Networking
{
    public interface IAishiKeysNetworkBridge : IDisposable
    {
        bool Active { get; }

        bool Ready { get; }

        bool IsServer { get; }

        bool IsHeadless { get; }

        bool Broadcast(string payload);
        bool SendToServer(string payload);
        void DrainReceivedPayloads(Action<string> handler);
    }
}
