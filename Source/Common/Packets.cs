namespace Multiplayer.Common
{
    public enum Packets : byte
    {
        ClientProtocol,
        ClientDefs,
        ClientUsername,
        ClientWorldReady,
        ClientCommand,
        ClientAutosavedData,
        ClientIdBlockRequest,
        ClientChat,
        ClientKeepAlive,
        ClientSteamRequest,
        ClientSyncInfo,
        ClientCursor,
        ClientDesynced,
        ClientPause,
        ClientDebug,
        ClientSelected,

        ServerModList,
        ServerDefsOk,
        ServerWorldData,
        ServerCommand,
        ServerMapResponse,
        ServerNotification,
        ServerTimeControl,
        ServerChat,
        ServerPlayerList,
        ServerKeepAlive,
        ServerSteamAccept,
        ServerSyncInfo,
        ServerCursor,
        ServerPause,
        ServerDebug,
        ServerSelected,

        Count,
        SpecialSteamDisconnect = 63 // Also the max packet id
    }

    public enum ConnectionStateEnum : byte
    {
        ClientJoining,
        ClientPlaying,
        ClientSteam,

        ServerJoining,
        ServerPlaying,
        ServerSteam, // unused

        Count,
        Disconnected
    }
}