using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Steamworks;

namespace Multiplayer.Client.Networking
{
    /// <summary>
    /// Class for handling the client -> server connection across steam. Uses <see cref="SteamNetworking"/>'s functions to transmit data.
    /// </summary>
    public class SteamClientToServerConnection : SteamBaseConnection
    {
        /// <summary>
        /// Constructor that clears the steam p2p channel and sends an initial test packet.
        /// </summary>
        /// <param name="remoteId">The steam id of the player hosting the server.</param>
        public SteamClientToServerConnection(CSteamID remoteId) : base(remoteId)
        {
            SteamIntegration.ClearChannel(0);

            SteamNetworking.SendP2PPacket(remoteId, new byte[] { 1 }, 1, EP2PSend.k_EP2PSendReliable, 0);
        }

        /// <summary>
        ///  Override to handle the <see cref="Packet.Special_Steam_Disconnect"/> packet to terminate the connection.
        /// </summary>
        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packet.Special_Steam_Disconnect)
                Multiplayer.session.HandleDisconnectReason((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            base.HandleReceive(msgId, fragState, reader, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            Multiplayer.session.disconnectReasonKey = error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "Connection timed out" : "Connection error";
            base.OnError(error);
        }

        protected override void OnDisconnect()
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected();
            OnMainThread.StopMultiplayer();
        }
    }
}