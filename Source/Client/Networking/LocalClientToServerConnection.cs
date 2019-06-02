using System;
using Multiplayer.Common;
using Multiplayer.Server.Networking;
using Verse;

namespace Multiplayer.Client.Networking
{
    public class ClientToServerLocalhostConnection : IMultiplayerConnection
    {
        public ServerToClientLocalhostConnection serverSide;

        public override int Latency { get => 0; set { } }

        public ClientToServerLocalhostConnection(string username)
        {
            this.username = username;
        }

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

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }
}