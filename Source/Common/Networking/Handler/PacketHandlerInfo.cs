using System.Reflection;

namespace Multiplayer.Common.Networking.Handler
{
    public class PacketHandlerInfo
    {
        public readonly MethodInfo method;
        public readonly bool fragment;

        public PacketHandlerInfo(MethodInfo method, bool fragment)
        {
            this.method = method;
            this.fragment = fragment;
        }
    }
}