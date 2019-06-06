using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ionic.Zlib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;
using Multiplayer.Server;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Networking.Handler
{
    public class ClientGameplayPacketHandler : MpPacketHandler
    {
        public ClientGameplayPacketHandler(BaseMultiplayerConnection connection) : base(connection)
        {
        }

        [HandlesPacket(Packet.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            var tickUntil = data.ReadInt32();
            TickPatch.tickUntil = tickUntil;
        }

        [HandlesPacket(Packet.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            var id = data.ReadInt32();
            var ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(Packet.Client_KeepAlive, id, (ticksBehind << 1) | (TickPatch.Skipping ? 1 : 0));
        }

        [HandlesPacket(Packet.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            var cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            OnMainThread.ScheduleCommand(cmd);
        }

        [HandlesPacket(Packet.Server_PlayerList)]
        public void HandlePlayerList(ByteReader data)
        {
            var action = (PlayerListAction) data.ReadByte();

            if (action == PlayerListAction.Add)
            {
                var info = PlayerListEntry.Read(data);
                if (!Multiplayer.session.players.Contains(info))
                    Multiplayer.session.players.Add(info);
            }
            else if (action == PlayerListAction.Remove)
            {
                var id = data.ReadInt32();
                Multiplayer.session.players.RemoveAll(p => p.id == id);
            }
            else if (action == PlayerListAction.List)
            {
                var count = data.ReadInt32();

                Multiplayer.session.players.Clear();
                for (var i = 0; i < count; i++)
                    Multiplayer.session.players.Add(PlayerListEntry.Read(data));
            }
            else if (action == PlayerListAction.Latencies)
            {
                var count = data.ReadInt32();

                for (var i = 0; i < count; i++)
                {
                    var player = Multiplayer.session.players[i];
                    player.latency = data.ReadInt32();
                    player.ticksBehind = data.ReadInt32();
                }
            }
            else if (action == PlayerListAction.Status)
            {
                var id = data.ReadInt32();
                var status = (ServerPlayer.Status) data.ReadByte();
                var player = Multiplayer.session.GetPlayerInfo(id);

                if (player != null)
                    player.status = status;
            }
        }

        [HandlesPacket(Packet.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            var msg = data.ReadString();
            Multiplayer.session.AddMsg(msg);
        }

        [HandlesPacket(Packet.Server_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            var playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            var seq = data.ReadByte();
            if (seq < player.cursorSeq && player.cursorSeq - seq < 128) return;

            var map = data.ReadByte();
            player.map = map;

            if (map == byte.MaxValue) return;

            var icon = data.ReadByte();
            var x = data.ReadShort() / 10f;
            var z = data.ReadShort() / 10f;

            player.cursorSeq = seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.Clock.ElapsedMillisDouble();
            player.cursorIcon = icon;

            var dragXRaw = data.ReadShort();
            if (dragXRaw != -1)
            {
                var dragX = dragXRaw / 10f;
                var dragZ = data.ReadShort() / 10f;

                player.dragStart = new Vector3(dragX, 0, dragZ);
            }
            else
            {
                player.dragStart = PlayerListEntry.Invalid;
            }
        }

        [HandlesPacket(Packet.Server_Selected)]
        public void HandleSelected(ByteReader data)
        {
            var playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            var reset = data.ReadBool();

            if (reset)
                player.selectedThings.Clear();

            var add = data.ReadPrefixedInts();
            for (var i = 0; i < add.Length; i++)
                player.selectedThings[add[i]] = Time.realtimeSinceStartup;

            var remove = data.ReadPrefixedInts();
            for (var i = 0; i < remove.Length; i++)
                player.selectedThings.Remove(remove[i]);
        }

        [HandlesPacket(Packet.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            var mapId = data.ReadInt32();

            var mapCmdsLen = data.ReadInt32();
            var mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (var j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            OnMainThread.cachedMapCmds[mapId] = mapCmds;

            var mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedMapData[mapId] = mapData;

            //ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [HandlesPacket(Packet.Server_Notification)]
        public void HandleNotification(ByteReader data)
        {
            var key = data.ReadString();
            var args = data.ReadPrefixedStrings();

            Messages.Message(key.Translate(Array.ConvertAll(args, s => (NamedArgument) s)),
                MessageTypeDefOf.SilentInput, false);
        }

        [HandlesPacket(Packet.Server_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.Deserialize(data));
        }

        [HandlesPacket(Packet.Server_Pause)]
        public void HandlePause(ByteReader data)
        {
            var pause = data.ReadBool();
            // This packet doesn't get processed in time during a synchronous long event 
        }

        [HandlesPacket(Packet.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
            var tick = data.ReadInt32();
            var start = data.ReadInt32();
            var end = data.ReadInt32();
            var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == tick);

            Log.Message($"{info?.desyncStackTraces.Count} arbiter traces");
            File.WriteAllText("arbiter_traces.txt",
                info?.GetFormattedStackTracesForRange(start, end, (start + end) / 2) ?? "null");
        }

        [HandlesPacket(Packet.Server_RequestRemoteStacks)]
        public void DumpStacksForRemoteDesyncedPlayer(ByteReader data)
        {
            var requesterId = data.ReadInt32();
            var tick = data.ReadInt32();
            var offset = data.ReadInt32();

            Log.Message($"Client #{requesterId} desynced and requested stacks at offset {offset} in tick {tick}");

            var writer = new ByteWriter();
            writer.WriteInt32(requesterId);

            //Identify which stacks were requested.
            var index = Multiplayer.game.sync.recentTraces.FindIndex(trace => trace.lastValidTick == tick) + offset;

            Multiplayer.game.sync.currentOpinion.desyncStackTraces = Multiplayer.game.sync.recentTraces;

            var stacks =
                Multiplayer.game.sync.currentOpinion.GetFormattedStackTracesForRange(index - 40, index + 40, index);

            Log.Message($"Gathered stacks; string is {stacks.Length} characters. Compressing and responding.");

            writer.WritePrefixedBytes(GZipStream.CompressString(stacks));

            Multiplayer.Client.Send(Packet.Client_ResponseRemoteStacks, writer.ToArray());
        }

        [HandlesPacket(Packet.Server_ResponseRemoteStacks)]
        public void OnReceiveRemoteStacks(ByteReader data)
        {
            var compressed = data.ReadPrefixedBytes();
            var stacks = GZipStream.UncompressString(compressed);

            DesyncReporter.remoteStacks = stacks;
            DesyncReporter.SaveLocalAndPromptUpload();
        }
    }
}