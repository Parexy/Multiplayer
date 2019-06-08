namespace Multiplayer.Common.Networking.Exception
{
    public class PacketSendException : System.Exception
    {
        public PacketSendException(string msg) : base(msg)
        {
        }
    }
}