using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;

namespace Multiplayer.Client.Networking.Handler
{
    public class ClientSteamRequestPacketHandler : MpPacketHandler
    {
        public ClientSteamRequestPacketHandler(BaseMultiplayerConnection connection) : base(connection)
        {
            //connection.Send(Packets.Client_SteamRequest);
        }

        [HandlesPacket(Packet.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientJoining;
        }
    }
}