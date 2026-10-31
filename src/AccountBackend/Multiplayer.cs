using Newtonsoft.Json.Linq;

namespace AccountBackend;

public interface IGameServerDirectory
{
    JObject List(long userId, JObject request);
    JObject Connect(long userId, JObject request);
}

public sealed class DisabledGameServerDirectory : IGameServerDirectory
{
    public JObject List(long userId, JObject request) => new() { ["items"]=new JArray() };
    public JObject Connect(long userId, JObject request) => throw new RequestError("multiplayer_not_implemented");
}

public record GamePacket(int Channel, ReadOnlyMemory<byte> Payload);
public interface IGamePacketBridge
{
    GamePacket MirrorToUnet(GamePacket packet);
    GamePacket UnetToMirror(GamePacket packet);
}
public sealed class UnimplementedGamePacketBridge : IGamePacketBridge
{
    public GamePacket MirrorToUnet(GamePacket packet) => throw new NotSupportedException("Mirror/UNet packet bridge is not implemented.");
    public GamePacket UnetToMirror(GamePacket packet) => throw new NotSupportedException("Mirror/UNet packet bridge is not implemented.");
}
