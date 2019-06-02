using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Steamworks;

namespace Multiplayer.Server.Networking
{
    public class SteamServerToClientConnection : SteamBaseConnection
    {
        public SteamServerToClientConnection(CSteamID remoteId) : base(remoteId)
        {
        }

        protected override void OnDisconnect()
        {
            serverPlayer.Server.OnDisconnected(this, MpDisconnectReason.ClientLeft);
        }
    }

}
