using Steamworks;

namespace Multiplayer.Common.Networking
{
    public abstract class SteamBaseConnection : IMultiplayerConnection
    {
        public readonly CSteamID remoteSteamId;

        public SteamBaseConnection(CSteamID remoteId)
        {
            remoteSteamId = remoteId;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            byte[] packet = new byte[1 + raw.Length];
            packet[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(packet, 1);

            SteamNetworking.SendP2PPacket(remoteSteamId, packet, (uint)packet.Length, reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable, 0);
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                OnDisconnect();
            }
            else
            {
                base.HandleReceive(msgId, fragState, reader, reliable);
            }
        }

        public virtual void OnError(EP2PSessionError error)
        {
            OnDisconnect();
        }

        protected abstract void OnDisconnect();

        public override string ToString()
        {
            return $"SteamP2P ({remoteSteamId}) ({username})";
        }
    }
}