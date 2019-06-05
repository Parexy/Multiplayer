namespace Multiplayer.Common.Networking.Handler
{
    /// <summary>
    /// Used to indicate which packet handler should be used at the current moment.
    /// </summary>
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
        /// Used in client code to indicate we're waiting for acceptance over steam.
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
        /// Special field to contain the number of values this enum has
        /// </summary>
        Count,
        /// <summary>
        /// Used either side to show a connection is not established.
        /// </summary>
        Disconnected
    }
}