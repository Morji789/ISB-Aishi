using Fika.Core.Networking.LiteNetLib.Utils;

namespace AishiKeys.Fika
{
    public struct AishiKeysPacket : INetSerializable
    {
        public string Payload;
        public void Serialize(NetDataWriter writer)
        {
            writer.PutLargeString(Payload ?? string.Empty);
        }

        public void Deserialize(NetDataReader reader)
        {
            Payload = reader.GetLargeString();
        }
    }
}
