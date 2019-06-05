using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using Multiplayer.Common.Networking;
using Verse;

namespace Multiplayer.Client.Desyncs
{
    public class ClientSyncOpinion
    {
        public bool isLocalClientsOpinion;

        public int startTick;
        public List<uint> commandRandomStates = new List<uint>();
        public List<uint> worldRandomStates = new List<uint>();
        public List<MapRandomStateData> mapStates = new List<MapRandomStateData>();

        public List<StackTraceLogItem> desyncStackTraces = new List<StackTraceLogItem>();
        public List<int> desyncStackTraceHashes = new List<int>();
        public bool simulating;
        public string username;

        public ClientSyncOpinion(int startTick)
        {
            this.startTick = startTick;
        }

        public string CheckForDesync(ClientSyncOpinion other)
        {
            if (!mapStates.Select(m => m.mapId).SequenceEqual(other.mapStates.Select(m => m.mapId)))
                return "Map instances don't match";

            for (var i = 0; i < mapStates.Count; i++)
            {
                if (!mapStates[i].randomStates.SequenceEqual(other.mapStates[i].randomStates))
                    return $"Wrong random state on map {mapStates[i].mapId}";
            }

            if (!worldRandomStates.SequenceEqual(other.worldRandomStates))
                return "Wrong random state for the world";

            if (!commandRandomStates.SequenceEqual(other.commandRandomStates))
                return "Random state from commands doesn't match";

            if (!simulating && !other.simulating && desyncStackTraceHashes.Any() && other.desyncStackTraceHashes.Any() && !desyncStackTraceHashes.SequenceEqual(other.desyncStackTraceHashes))
                return "Trace hashes don't match";

            return null;
        }

        public List<uint> GetRandomStatesForMap(int mapId)
        {
            var result = mapStates.Find(m => m.mapId == mapId);
            if (result != null) return result.randomStates;
            mapStates.Add(result = new MapRandomStateData(mapId));
            return result.randomStates;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(startTick);
            writer.WritePrefixedUInts(commandRandomStates);
            writer.WritePrefixedUInts(worldRandomStates);

            writer.WriteInt32(mapStates.Count);
            foreach (var map in mapStates)
            {
                writer.WriteInt32(map.mapId);
                writer.WritePrefixedUInts(map.randomStates);
            }

            writer.WritePrefixedInts(desyncStackTraceHashes);
            writer.WriteBool(simulating);

            //Write our name for debugging purposes
            writer.WriteString(Multiplayer.username);

            return writer.ToArray();
        }

        public static ClientSyncOpinion Deserialize(ByteReader data)
        {
            var startTick = data.ReadInt32();

            var cmds = new List<uint>(data.ReadPrefixedUInts());
            var world = new List<uint>(data.ReadPrefixedUInts());

            var maps = new List<MapRandomStateData>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                var mapData = new List<uint>(data.ReadPrefixedUInts());
                maps.Add(new MapRandomStateData(mapId) { randomStates = mapData });
            }

            var traceHashes = new List<int>(data.ReadPrefixedInts());
            var playing = data.ReadBool();

            var name = data.ReadString();

            return new ClientSyncOpinion(startTick)
            {
                commandRandomStates = cmds,
                worldRandomStates = world,
                mapStates = maps,
                desyncStackTraceHashes = traceHashes,
                simulating = playing,
                username = name
            };
        }

        public void TryMarkSimulating()
        {
            if (TickPatch.Skipping)
                simulating = true;
        }

        /// <summary>
        /// Returns a string form of 
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="diffAt"></param>
        /// <returns></returns>
        public string GetFormattedStackTracesForRange(int start, int end, int diffAt)
        {
            var traces = desyncStackTraces
                .Skip(Math.Max(0, start))
                .Take(end - start)
                .ToList();

            diffAt -= start;

            var builder = new StringBuilder();
            for(var i = 0; i < traces.Count; i++)
            {
                var trace = traces[i];
                
                if (i == diffAt)
                    builder.Append("===desynchere===\n\n");
                
                builder.Append(trace.additionalInfo + "\n" + trace.stackTrace.Join(m => m.MethodDesc(), "\n") + "\n\n");

                if (i == diffAt)
                    builder.Append("===desyncfin===\n\n");
            }

            return builder.ToString();
        }
    }
}