using System.Collections.Generic;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Server;
using UnityEngine;

namespace Multiplayer.Client
{
    public class PlayerListEntry
    {
        public static readonly Vector3 Invalid = new Vector3(-1, 0, -1);

        public int id;
        public string username;
        public int latency;
        public int ticksBehind;
        public ServerPlayer.Type type;
        public ServerPlayer.Status status;

        public ulong steamId;
        public string steamPersonaName;

        public byte cursorSeq;
        public byte map = byte.MaxValue;
        public Vector3 cursor;
        public Vector3 lastCursor;
        public double updatedAt;
        public double lastDelta;
        public byte cursorIcon;
        public Vector3 dragStart = Invalid;

        public Dictionary<int, float> selectedThings = new Dictionary<int, float>();

        private PlayerListEntry(int id, string username, int latency, ServerPlayer.Type type)
        {
            this.id = id;
            this.username = username;
            this.latency = latency;
            this.type = type;
        }

        public static PlayerListEntry Read(ByteReader data)
        {
            int id = data.ReadInt32();
            string username = data.ReadString();
            int latency = data.ReadInt32();
            var type = (ServerPlayer.Type)data.ReadByte();
            var status = (ServerPlayer.Status)data.ReadByte();

            var steamId = data.ReadULong();
            var steamName = data.ReadString();

            var ticksBehind = data.ReadInt32();

            return new PlayerListEntry(id, username, latency, type)
            {
                status = status,
                steamId = steamId,
                steamPersonaName = steamName,
                ticksBehind = ticksBehind
            };
        }
    }
}