using System;
using Multiplayer.Client;
using Multiplayer.Client.Networking;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Verse;

namespace Multiplayer.Server.Networking
{
    /// <summary>
    /// Class for handling a connection to the "client" side of a game instance, when said instance is hosting a server.
    /// Doesn't actually send any information across the network, even locally, just calls into the client thread. 
    /// </summary>
    public class ServerToClientLocalhostConnection : BaseMultiplayerConnection
    {
        public ClientToServerLocalhostConnection clientSide;

        public override int Latency { get => 0; set { } }

        public ServerToClientLocalhostConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    clientSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {clientSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }
}