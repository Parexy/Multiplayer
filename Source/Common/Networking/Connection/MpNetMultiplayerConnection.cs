using LiteNetLib;

namespace Multiplayer.Common.Networking.Connection
{
    public class MpNetMultiplayerConnection : BaseMultiplayerConnection
    {
        public readonly NetPeer peer;

        public MpNetMultiplayerConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            peer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            peer.Flush();
            peer.NetManager.DisconnectPeer(peer, GetDisconnectBytes(reason, data));
        }

        public override string ToString()
        {
            return $"NetConnection ({peer.EndPoint}) ({username})";
        }
    }
}