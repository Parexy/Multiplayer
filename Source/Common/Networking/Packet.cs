namespace Multiplayer.Common.Networking
{
    /// <summary>
    /// Packets prefixed with Client_ are sent by the client, those prefixed with Server_ are sent by the server
    /// </summary>
    public enum Packet : byte
    {
        /// <summary>
        /// Used to send the client's <see cref="MpVersion.Protocol"/> version
        /// </summary>
        Client_Protocol,
        /// <summary>
        /// Used to send the number of entries and hash of each DefDatabase to the server to verify data 
        /// </summary>
        Client_Defs,
        /// <summary>
        /// Used to send the client's name to the server
        /// </summary>
        Client_Username,
        /// <summary>
        /// Used to tell the server that the simulating... phase has completed.
        /// </summary>
        Client_WorldReady,
        /// <summary>
        /// Used to send a command to the server
        /// </summary>
        Client_Command,
        /// <summary>
        /// Used to send the server the data that has been autosaved.
        /// </summary>
        Client_AutosavedData,
        /// <summary>
        /// Used to ask the server to block a steam id. Currently seems to be unimplemented?
        /// </summary>
        Client_IdBlockRequest,
        /// <summary>
        /// Used to send a chat message.
        /// </summary>
        Client_Chat,
        /// <summary>
        /// Used to keep the connection alive and tell the server how many ticks behind we are, and if we're simulating
        /// </summary>
        Client_KeepAlive,
        /// <summary>
        /// Unused
        /// </summary>
        Client_SteamRequest,
        /// <summary>
        /// Used to send our local <see cref="Multiplayer.Client.Desyncs.ClientSyncOpinion"/> to the server.
        /// </summary>
        Client_SyncInfo,
        /// <summary>
        /// Used once per frame to send the cursor position to the server.
        /// </summary>
        Client_Cursor,
        /// <summary>
        /// Used to tell the server that we desynced - which sets our name in the chat GUI to red.
        /// </summary>
        Client_Desynced,
        /// <summary>
        /// Used to tell the server to pause the game
        /// </summary>
        Client_Pause,
        /// <summary>
        /// Used to tell the arbiter to dump its stacks to disk in the event of a desync.
        /// </summary>
        Client_Debug,
        /// <summary>
        /// Used once per frame to tell the server what we have selected.
        /// </summary>
        Client_Selected,

        /// <summary>
        /// Used to tell the client the server's rimworld version and the names of mods
        /// </summary>
        Server_ModList,
        /// <summary>
        /// Used to tell the client that its defs match ours.
        /// </summary>
        Server_DefsOK,
        /// <summary>
        /// Used to give the client the world and maps.
        /// </summary>
        Server_WorldData,
        /// <summary>
        /// Used to inform all clients of a command sent using <see cref="Client_Command"/> packet
        /// </summary>
        Server_Command,
        /// <summary>
        /// Appears to be unusued
        /// </summary>
        Server_MapResponse,
        /// <summary>
        /// Used to send clients notifications, currently players joining and leaving
        /// </summary>
        Server_Notification,
        /// <summary>
        /// Used to tell all clients which tick te server is on
        /// </summary>
        Server_TimeControl,
        /// <summary>
        /// Used to send chat messages, usually in response to a <see cref="Client_Chat"/> packet but also as feedback for commands.
        /// </summary>
        Server_Chat,
        /// <summary>
        /// Used to tell clients the player list.
        /// </summary>
        Server_PlayerList,
        /// <summary>
        /// Called every 180 network ticks, to both check clients are still there, and to ask them what tick they're on.
        /// </summary>
        Server_KeepAlive,
        /// <summary>
        /// Used to accept a steam connection
        /// </summary>
        Server_SteamAccept,
        /// <summary>
        /// Used to inform all clients of a client's new <see cref="Multiplayer.Client.Desyncs.ClientSyncOpinion"/>
        /// </summary>
        Server_SyncInfo,
        /// <summary>
        /// Used to tell all clients a client's cursor position in response to a <see cref="Client_Cursor"/> packet.
        /// </summary>
        Server_Cursor,
        /// <summary>
        /// Used to tell clients to pause after a <see cref="Client_Pause"/> packet.
        /// </summary>
        Server_Pause,
        /// <summary>
        /// Sent to the arbiter to tell it to dump its stacks.
        /// </summary>
        Server_Debug,
        /// <summary>
        /// Used to tell clients what a client has selected after a <see cref="Client_Selected"/> packet.
        /// </summary>
        Server_Selected,

        /// <summary>
        /// Special field used to keep track of how many packets there are.
        /// </summary>
        Count,
        /// <summary>
        /// Special packet to inform either side of a steam disconnect.
        /// </summary>
        Special_Steam_Disconnect = 63 // Also the max packet id
    }
}
