#region

using System;

#endregion

namespace Multiplayer.Common
{
    public enum CommandType : byte
    {
        // Global scope
        WorldTimeSpeed,
        Autosave,
        SetupFaction,
        GlobalIdBlock,
        FactionOnline,
        FactionOffline,

        // Mixed scope
        Sync,
        DebugTools,

        // Map scope
        MapTimeSpeed,
        CreateMapFactionData,
        MapIdBlock,
        Forbid,
        Designator,
        SpawnPawn,
    }

    public class ScheduledCommand
    {
        public const int NoFaction = -1;
        public const int Global = -1;
        public readonly byte[] data;
        public readonly int factionId;
        public readonly int mapId;
        public readonly int ticks;

        public readonly CommandType type;

        // Client only, not serialized
        public bool issuedBySelf;

        public ScheduledCommand(CommandType type, int ticks, int factionId, int mapId, byte[] data)
        {
            this.type = type;
            this.ticks = ticks;
            this.factionId = factionId;
            this.mapId = mapId;
            this.data = data;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(type));
            writer.WriteInt32(ticks);
            writer.WriteInt32(factionId);
            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(data);

            return writer.ToArray();
        }

        public static ScheduledCommand Deserialize(ByteReader data)
        {
            var cmd = (CommandType) data.ReadInt32();
            var ticks = data.ReadInt32();
            var factionId = data.ReadInt32();
            var mapId = data.ReadInt32();
            var extraBytes = data.ReadPrefixedBytes();

            return new ScheduledCommand(cmd, ticks, factionId, mapId, extraBytes);
        }
    }
}