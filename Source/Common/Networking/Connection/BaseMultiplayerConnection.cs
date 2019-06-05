using System;
using Multiplayer.Common.Networking.Exception;
using Multiplayer.Common.Networking.Handler;
using Multiplayer.Server;

namespace Multiplayer.Common.Networking.Connection
{
    /// <summary>
    /// There's one of these for each connection, so one on the client, for the connection to the server, and one
    /// per player on the server.
    /// </summary>
    public abstract class BaseMultiplayerConnection
    {
        public string username;
        public ServerPlayer serverPlayer;

        public virtual int Latency { get; set; }

        public ConnectionStateEnum State
        {
            get => currentPacketHandler;

            set
            {
                currentPacketHandler = value;

                if (currentPacketHandler == ConnectionStateEnum.Disconnected)
                    packetHandler = null;
                else
                    packetHandler = (MpPacketHandler) Activator.CreateInstance(MpPacketHandler.connectionImpls[(int) value], this);
            }
        }

        public MpPacketHandler PacketHandler => packetHandler;

        private ConnectionStateEnum currentPacketHandler;
        private MpPacketHandler packetHandler;

        public virtual void Send(Packet id)
        {
            Send(id, new byte[0]);
        }

        public virtual void Send(Packet id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public virtual void Send(Packet id, byte[] message, bool reliable = true)
        {
            if (currentPacketHandler == ConnectionStateEnum.Disconnected)
                return;

            if (message.Length > FragmentSize)
                throw new PacketSendException($"Packet {id} too big for sending ({message.Length}>{FragmentSize})");

            byte[] full = new byte[1 + message.Length];
            full[0] = (byte) (Convert.ToByte(id) & 0x3F);
            message.CopyTo(full, 1);

            SendRaw(full, reliable);
        }

        // Steam doesn't like messages bigger than a megabyte
        public const int FragmentSize = 50_000;
        public const int MaxPacketSize = 33_554_432;

        private const int FRAG_NONE = 0x0;
        private const int FRAG_MORE = 0x40;
        private const int FRAG_END = 0x80;

        // All fragmented packets need to be sent from the same thread
        public virtual void SendFragmented(Packet id, byte[] message)
        {
            if (currentPacketHandler == ConnectionStateEnum.Disconnected)
                return;

            int read = 0;
            while (read < message.Length)
            {
                int len = Math.Min(FragmentSize, message.Length - read);
                int fragState = (read + len >= message.Length) ? FRAG_END : FRAG_MORE;
                byte state = (byte) ((Convert.ToByte(id) & 0x3F) | fragState);

                var writer = new ByteWriter(1 + 4 + len);

                // Write the packet id and fragment state: MORE or END
                writer.WriteByte(state);

                // Send the message length with the first packet
                if (read == 0) writer.WriteInt32(message.Length);

                // Copy the message fragment
                writer.WriteFrom(message, read, len);

                SendRaw(writer.ToArray());

                read += len;
            }
        }

        protected abstract void SendRaw(byte[] raw, bool reliable = true);

        public virtual void HandleReceive(ByteReader data, bool reliable)
        {
            if (currentPacketHandler == ConnectionStateEnum.Disconnected)
                return;

            if (data.Left == 0)
                throw new PacketReadException("No packet id");

            byte info = data.ReadByte();
            byte msgId = (byte) (info & 0x3F);
            byte fragState = (byte) (info & 0xC0);

            HandleReceive(msgId, fragState, data, reliable);
        }

        private ByteWriter fragmentedWriter;
        private int fullSize; // For information, doesn't affect anything

        public int FragmentProgress => (fragmentedWriter?.Position * 100 / fullSize) ?? 0;

        protected virtual void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId < 0 || msgId >= MpPacketHandler.packetHandlerMethods.Length)
                throw new PacketReadException($"Bad packet id {msgId}");

            Packet packetId = (Packet) msgId;

            var handler = MpPacketHandler.packetHandlerMethods[(int) currentPacketHandler, (int) packetId];
            if (handler == null)
            {
                if (reliable)
                    throw new PacketReadException($"No method for packet {packetId} in handler {currentPacketHandler}");
                else
                    return;
            }

            if (fragState != FRAG_NONE && fragmentedWriter == null)
                fullSize = reader.ReadInt32();

            if (reader.Left > FragmentSize)
                throw new PacketReadException($"Packet {packetId} too big {reader.Left}>{FragmentSize}");

            if (fragState == FRAG_NONE)
            {
                handler.method.Invoke(packetHandler, new object[] {reader});
            }
            else if (!handler.fragment)
            {
                throw new PacketReadException($"Packet {packetId} can't be fragmented");
            }
            else
            {
                if (fragmentedWriter == null)
                    fragmentedWriter = new ByteWriter(reader.Left);

                fragmentedWriter.WriteRaw(reader.ReadRaw(reader.Left));

                if (fragmentedWriter.Position > MaxPacketSize)
                    throw new PacketReadException($"Full packet {packetId} too big {fragmentedWriter.Position}>{MaxPacketSize}");

                if (fragState == FRAG_END)
                {
                    handler.method.Invoke(packetHandler, new object[] {new ByteReader(fragmentedWriter.ToArray())});
                    fragmentedWriter = null;
                }
            }
        }

        public abstract void Close(MpDisconnectReason reason = MpDisconnectReason.Generic, byte[] data = null);

        public static byte[] GetDisconnectBytes(MpDisconnectReason reason, byte[] data = null)
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte) reason);
            writer.WritePrefixedBytes(data ?? new byte[0]);
            return writer.ToArray();
        }
    }
}