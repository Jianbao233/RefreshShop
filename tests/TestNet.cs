using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Platform;

// Transport fixture only: message serialization, location buffering and receivers
// are the actual game implementations. The peer registers no RefreshShop handler.
internal sealed class TestNet : INetGameService
{
    internal TestNet Peer;
    internal readonly List<INetMessage> Sent = new();
    internal bool FailSend;
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    public ulong NetId { get; set; }
    public bool IsConnected { get; set; } = true;
    public bool IsGameLoading { get; private set; }
    public NetGameType Type { get; set; }
    public PlatformType Platform => PlatformType.Steam;
#if !LEGACY_GAME_API
    public PeerVersionInfo LocalVersion => default;
#endif
    public event Action<NetErrorInfo> Disconnected { add { } remove { } }
    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage => SendMessage(message);
    public void SendMessage<T>(T message) where T : INetMessage
    {
        if (FailSend) throw new IOException("Injected transport failure");
        Sent.Add(message);
        if (Peer == null) return;
        var writer = new PacketWriter();
        ((IPacketSerializable)message).Serialize(writer);
        var bytes = writer.Buffer.Take(writer.BytePosition).ToArray();
        var reader = new PacketReader(); reader.Reset(bytes);
        var decoded = (IPacketSerializable)Activator.CreateInstance(typeof(T));
        decoded.Deserialize(reader);
        if (Peer._handlers.TryGetValue(typeof(T), out var handlers))
            foreach (var handler in handlers.ToArray()) handler.DynamicInvoke(decoded, NetId);
    }
    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers)) _handlers[typeof(T)] = handlers = new();
        handlers.Add(handler);
    }
    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    { if (_handlers.TryGetValue(typeof(T), out var handlers)) handlers.Remove(handler); }
    public void Update() { }
    public void Disconnect(NetError reason, bool now = false) => IsConnected = false;
    public ConnectionStats GetStatsForPeer(ulong peerId) => null;
    public void SetGameLoading(bool loading) => IsGameLoading = loading;
    public void SetBufferMessages(bool buffer) { }
    public string GetRawLobbyIdentifier() => null;
}
