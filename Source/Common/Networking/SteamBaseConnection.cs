using Steamworks;

namespace Multiplayer.Common.Networking
{
    /// <summary>
    /// Base class for either client -> server or server -> client connections that are routed through the steam friends network.
    /// </summary>
    public abstract class SteamBaseConnection : IMultiplayerConnection
    {
        /// <summary>
        /// The steam ID of the remote.
        /// </summary>
        public readonly CSteamID remoteSteamId;

        public SteamBaseConnection(CSteamID remoteId)
        {
            remoteSteamId = remoteId;
        }

        /// <summary>
        /// Sends packets through the steam network
        /// </summary>
        /// <param name="raw">The raw data contained within the packet</param>
        /// <param name="reliable">Set to true to tell steam to send this packet reliably.</param>
        protected override void SendRaw(byte[] raw, bool reliable)
        {
            byte[] packet = new byte[1 + raw.Length];
            packet[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(packet, 1);

            SteamNetworking.SendP2PPacket(remoteSteamId, packet, (uint)packet.Length, reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable, 0);
        }

        /// <summary>
        /// Overriden to send a <see cref="Packets.Special_Steam_Disconnect"/> packet instead of just closing the connection
        /// </summary>
        /// <param name="reason">Why we're closing this connection</param>
        /// <param name="data">Additional data to send with the DC packet.</param>
        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        /// <summary>
        /// Overriden to handle <see cref="Packets.Special_Steam_Disconnect"/> packets.
        /// </summary>
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