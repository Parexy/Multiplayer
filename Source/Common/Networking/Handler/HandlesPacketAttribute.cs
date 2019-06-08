using System;

namespace Multiplayer.Common.Networking.Handler
{
    public class HandlesPacketAttribute : Attribute
    {
        public readonly Packet packet;

        public HandlesPacketAttribute(Packet packet)
        {
            this.packet = packet;
        }
    }
}