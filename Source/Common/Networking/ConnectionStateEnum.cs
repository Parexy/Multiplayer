namespace Multiplayer.Common.Networking
{
    public enum ConnectionStateEnum : byte
    {
        /// <summary>
        /// Used in client code to indicate we're joining the server
        /// </summary>
        ClientJoining,
        /// <summary>
        /// Used in client code to indicate we're playing on the server
        /// </summary>
        ClientPlaying,
        /// <summary>
        /// Used in client code to indicate we're playing through a steam connection
        /// </summary>
        ClientSteam,

        /// <summary>
        /// Used on the server to indicate a connection is a client joining the server
        /// </summary>
        ServerJoining,
        /// <summary>
        /// Used on the server to indicate a connection is a client playing on the server
        /// </summary>
        ServerPlaying,
        /// <summary>
        /// Unused
        /// </summary>
        ServerSteam,

        /// <summary>
        /// Special field to contain the number of values this enum has
        /// </summary>
        Count,
        /// <summary>
        /// Used either side to show a connection is not established.
        /// </summary>
        Disconnected
    }
}