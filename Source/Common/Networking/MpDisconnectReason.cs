namespace Multiplayer.Common.Networking
{
    public enum MpDisconnectReason : byte
    {
        Generic,
        Protocol,
        Defs,
        UsernameLength,
        UsernameChars,
        UsernameAlreadyOnline,
        ServerClosed,
        ServerFull,
        Kick,
        ClientLeft,
    }
}