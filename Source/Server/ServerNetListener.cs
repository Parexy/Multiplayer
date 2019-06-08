using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;

namespace Multiplayer.Server
{
    public class ServerNetListener : INetEventListener
    {
        private MultiplayerServer server;
        private bool isArbiterListener;

        public ServerNetListener(MultiplayerServer server, bool isArbiterListener)
        {
            this.server = server;
            this.isArbiterListener = isArbiterListener;
        }

        public void OnConnectionRequest(ConnectionRequest req)
        {
            if (!isArbiterListener && server.settings.maxPlayers > 0 && server.players.Count(p => !p.IsArbiter) >= server.settings.maxPlayers)
            {
                req.Reject(BaseMultiplayerConnection.GetDisconnectBytes(MpDisconnectReason.ServerFull));
                return;
            }

            req.Accept();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            BaseMultiplayerConnection conn = new MpNetConnection(peer);
            conn.State = ConnectionStateEnum.ServerJoining;
            peer.Tag = conn;

            var player = server.OnConnected(conn);
            if (isArbiterListener)
            {
                player.type = ServerPlayer.Type.Arbiter;
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            BaseMultiplayerConnection conn = peer.GetConnection();
            server.OnDisconnected(conn, MpDisconnectReason.ClientLeft);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            peer.GetConnection().Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            peer.GetConnection().serverPlayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}