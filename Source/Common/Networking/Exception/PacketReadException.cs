namespace Multiplayer.Common.Networking.Exception
{
    public class PacketReadException : System.Exception
    {
        public PacketReadException(string msg) : base(msg)
        {
        }
    }
}