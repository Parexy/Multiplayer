using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Steamworks;

namespace Multiplayer.Client.Networking
{
    public class SteamClientToServerConnection : SteamBaseConnection
    {
        public SteamClientToServerConnection(CSteamID remoteId) : base(remoteId)
        {
            SteamIntegration.ClearChannel(0);

            SteamNetworking.SendP2PPacket(remoteId, new byte[] { 1 }, 1, EP2PSend.k_EP2PSendReliable, 0);
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
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