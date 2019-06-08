using System;
using System.Reflection;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Server;

namespace Multiplayer.Common.Networking.Handler
{
    public abstract class MpPacketHandler
    {
        public readonly BaseMultiplayerConnection connection;

        protected ServerPlayer Player => connection.serverPlayer;
        protected MultiplayerServer Server => MultiplayerServer.instance;

        public MpPacketHandler(BaseMultiplayerConnection connection)
        {
            this.connection = connection;
        }

        public static Type[] connectionImpls = new Type[(int) ConnectionStateEnum.Count];
        public static PacketHandlerInfo[,] packetHandlerMethods = new PacketHandlerInfo[(int) ConnectionStateEnum.Count, (int) Packet.Count];

        public static void SetPacketHandlerForState(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpPacketHandler))) return;

            connectionImpls[(int) state] = type;

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetAttribute<HandlesPacketAttribute>();
                if (attr == null)
                    continue;

                if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(ByteReader))
                    continue;

                bool fragment = method.GetAttribute<IsFragmentedAttribute>() != null;
                packetHandlerMethods[(int) state, (int) attr.packet] = new PacketHandlerInfo(method, fragment);
            }
        }
    }
}