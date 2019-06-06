using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;
using Verse;

namespace Multiplayer.Client.Networking
{
    /// <summary>
    /// Client-side listener for LiteNetLib events
    /// </summary>
    public class ClientNetListener : INetEventListener
    {
        /// <summary>
        /// Called by LitNetLib when the client successfully establishes a connection to the server
        /// </summary>
        /// <param name="peer"></param>
        public void OnPeerConnected(NetPeer peer)
        {
            //Set up a connection instance for this connection
            BaseMultiplayerConnection conn = new MpNetConnection(peer);
            conn.username = Multiplayer.username;
            conn.State = ConnectionStateEnum.ClientJoining;

            Multiplayer.session.client = conn;
            Multiplayer.session.ForceAllowRunInBackground();

            MpLog.Log("Net client connected");
        }

        /// <summary>
        /// Called when the connection to the server errors. 
        /// </summary>
        /// <param name="endPoint">Information on the IP and port we were connected to.</param>
        /// <param name="error">Enum - the error that occurred.</param>
        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            Log.Warning($"Net client error {error}");
        }

        /// <summary>
        /// Called when data is received from a peer.
        /// </summary>
        /// <param name="peer">The peer that sent that data</param>
        /// <param name="reader">A <see cref="NetPacketReader"/> that contains the data received</param>
        /// <param name="method">The <see cref="DeliveryMethod"/> used to the send the data.</param>
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            Multiplayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// Called when the connection is closed cleanly by the remote end.
        /// </summary>
        /// <param name="peer">The peer that closed the connection</param>
        /// <param name="info">Information about the disconnect event.</param>
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
            Multiplayer.session.HandleDisconnectReason((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            OnMainThread.StopMultiplayer();
            MpLog.Log("Net client disconnected");
        }

        private static string DisconnectReasonString(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ConnectionFailed: return "Connection failed";
                case DisconnectReason.ConnectionRejected: return "Connection rejected";
                case DisconnectReason.Timeout: return "Timed out";
                case DisconnectReason.HostUnreachable: return "Host unreachable";
                case DisconnectReason.InvalidProtocol: return "Invalid library protocol";
                default: return "Disconnected";
            }
        }

        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}