using System;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Server.Networking;
using Verse;

namespace Multiplayer.Client.Networking
{
    /// <summary>
    /// Class for handling a connection to the local server, when our instance of the game is hosting a server.
    /// Doesn't actually send any information across the network, even locally, just calls into the server thread. 
    /// </summary>
    public class ClientToServerLocalhostConnection : BaseMultiplayerConnection
    {
        public ServerToClientLocalhostConnection serverSide;

        /// <summary>
        /// Latency to localhost is always 0
        /// </summary>
        public override int Latency { get => 0; set { } }

        public ClientToServerLocalhostConnection(string username)
        {
            this.username = username;
        }

        /// <summary>
        /// "Send" data to the server by calling <see cref="BaseMultiplayerConnection.HandleReceive"/> on the server thread with the data.
        /// </summary>
        /// <param name="raw">The data to send</param>
        /// <param name="reliable">If this data should be sent reliably.</param>
        protected override void SendRaw(byte[] raw, bool reliable)
        {
            Multiplayer.LocalServer.Enqueue(() =>
            {
                try
                {
                    serverSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {serverSide}: {e}");
                }
            });
        }

        /// <summary>
        /// Does nothing, as you can't close the local connection
        /// </summary>
        public override void Close(MpDisconnectReason reason = MpDisconnectReason.Generic, byte[] data = null)
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }
}