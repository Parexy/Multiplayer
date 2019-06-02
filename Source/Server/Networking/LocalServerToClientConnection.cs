using System;
using Multiplayer.Client;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Server.Networking
{
    public class ServerToClientLocalhostConnection : IMultiplayerConnection
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