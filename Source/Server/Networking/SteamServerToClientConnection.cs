using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Steamworks;

namespace Multiplayer.Server.Networking
{
    /// <summary>
    /// Minimal class to handle the server side of a steam connection - i.e. just the disconnect packet.
    /// </summary>
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
