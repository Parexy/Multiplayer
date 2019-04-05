#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Profile;

#endregion

namespace Multiplayer.Client
{
    public class ClientJoiningState : MpConnectionState
    {
        public JoiningState state = JoiningState.Connected;

        public ClientJoiningState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.Client_Protocol, MpVersion.Protocol);

            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [PacketHandler(Packets.Server_ModList)]
        public void HandleModList(ByteReader data)
        {
            Multiplayer.session.mods.remoteRwVersion = data.ReadString();
            Multiplayer.session.mods.remoteModNames = data.ReadPrefixedStrings();

            var defs = Multiplayer.localDefInfos;
            Multiplayer.session.mods.defInfo = defs;

            var response = new ByteWriter();
            response.WriteInt32(defs.Count);

            foreach (var kv in defs)
            {
                response.WriteString(kv.Key);
                response.WriteInt32(kv.Value.count);
                response.WriteInt32(kv.Value.hash);
            }

            connection.Send(Packets.Client_Defs, response.ToArray());
        }

        [PacketHandler(Packets.Server_DefsOK)]
        public void HandleDefsOK(ByteReader data)
        {
            Multiplayer.session.gameName = data.ReadString();
            Multiplayer.session.playerId = data.ReadInt32();

            connection.Send(Packets.Client_Username, Multiplayer.username);

            state = JoiningState.Downloading;
        }

        [PacketHandler(Packets.Server_WorldData)]
        [IsFragmented]
        public void HandleWorldData(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientPlaying;
            Log.Message("Game data size: " + data.Length);

            var factionId = data.ReadInt32();
            Multiplayer.session.myFactionId = factionId;

            var tickUntil = data.ReadInt32();

            var worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedGameData = worldData;

            var mapsToLoad = new List<int>();

            var mapCmdsCount = data.ReadInt32();
            for (var i = 0; i < mapCmdsCount; i++)
            {
                var mapId = data.ReadInt32();

                var mapCmdsLen = data.ReadInt32();
                var mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (var j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                OnMainThread.cachedMapCmds[mapId] = mapCmds;
            }

            var mapDataCount = data.ReadInt32();
            for (var i = 0; i < mapDataCount; i++)
            {
                var mapId = data.ReadInt32();
                var rawMapData = data.ReadPrefixedBytes();

                var mapData = GZipStream.UncompressBuffer(rawMapData);
                OnMainThread.cachedMapData[mapId] = mapData;
                mapsToLoad.Add(mapId);
            }

            TickPatch.tickUntil = tickUntil;

            TickPatch.SkipTo(
                toTickUntil: true,
                onFinish: () => Multiplayer.Client.Send(Packets.Client_WorldReady),
                cancelButtonKey: "Quit",
                onCancel: GenScene.GoToMainMenu
            );

            ReloadGame(mapsToLoad);
        }

        private static XmlDocument GetGameDocument(List<int> mapsToLoad)
        {
            var gameDoc = ScribeUtil.LoadDocument(OnMainThread.cachedGameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (var map in mapsToLoad)
                using (var reader = XmlReader.Create(new MemoryStream(OnMainThread.cachedMapData[map])))
                {
                    var mapNode = gameDoc.ReadNode(reader);
                    gameNode["maps"].AppendChild(mapNode);

                    if (gameNode["currentMapIndex"] == null)
                        gameNode.AddNode("currentMapIndex", map.ToString());
                }

            return gameDoc;
        }

        public static void ReloadGame(List<int> mapsToLoad, bool async = true)
        {
            LoadPatch.gameToLoad = GetGameDocument(mapsToLoad);
            TickPatch.replayTimeSpeed = TimeSpeed.Paused;

            if (async)
                LongEventHandler.QueueLongEvent(() =>
                {
                    MemoryUtility.ClearAllMapsAndWorld();
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = "server";

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        LongEventHandler.QueueLongEvent(() => PostLoad(), "MpSimulating", false, null);
                    });
                }, "Play", "MpLoading", true, null);
            else
                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.LoadInMainThread(LoadPatch.gameToLoad);
                    PostLoad();
                }, "MpLoading", false, null);
        }

        private static void PostLoad()
        {
            // If the client gets disconnected during loading
            if (Multiplayer.Client == null) return;

            OnMainThread.cachedAtTime = TickPatch.Timer;
            Multiplayer.session.replayTimerStart = TickPatch.Timer;

            var factionData = Multiplayer.WorldComp.factionData.GetValueSafe(Multiplayer.session.myFactionId);
            if (factionData != null && factionData.online)
                Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(factionData.factionId);
            else
                Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;

            // todo find a better way
            Multiplayer.game.myFactionLoading = null;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(
                OnMainThread.cachedMapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }
    }

    public enum JoiningState
    {
        Connected,
        Downloading
    }

    public class ClientPlayingState : MpConnectionState
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            var tickUntil = data.ReadInt32();
            TickPatch.tickUntil = tickUntil;
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            var id = data.ReadInt32();
            var ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(Packets.Client_KeepAlive, id, (ticksBehind << 1) | (TickPatch.Skipping ? 1 : 0));
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            var cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            OnMainThread.ScheduleCommand(cmd);
        }

        [PacketHandler(Packets.Server_PlayerList)]
        public void HandlePlayerList(ByteReader data)
        {
            var action = (PlayerListAction) data.ReadByte();

            if (action == PlayerListAction.Add)
            {
                var info = PlayerInfo.Read(data);
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
                    Multiplayer.session.players.Add(PlayerInfo.Read(data));
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
                var status = (PlayerStatus) data.ReadByte();
                var player = Multiplayer.session.GetPlayerInfo(id);

                if (player != null)
                    player.status = status;
            }
        }

        [PacketHandler(Packets.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            var msg = data.ReadString();
            Multiplayer.session.AddMsg(msg);
        }

        [PacketHandler(Packets.Server_Cursor)]
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
                player.dragStart = PlayerInfo.Invalid;
            }
        }

        [PacketHandler(Packets.Server_Selected)]
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

        [PacketHandler(Packets.Server_MapResponse)]
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

        [PacketHandler(Packets.Server_Notification)]
        public void HandleNotification(ByteReader data)
        {
            var key = data.ReadString();
            var args = data.ReadPrefixedStrings();

            Messages.Message(key.Translate(Array.ConvertAll(args, s => (NamedArgument) s)),
                MessageTypeDefOf.SilentInput, false);
        }

        [PacketHandler(Packets.Server_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.Add(SyncInfo.Deserialize(data));
        }

        [PacketHandler(Packets.Server_Pause)]
        public void HandlePause(ByteReader data)
        {
            var pause = data.ReadBool();
            // This packet doesn't get processed in time during a synchronous long event 
        }

        [PacketHandler(Packets.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
            var tick = data.ReadInt32();
            var start = data.ReadInt32();
            var end = data.ReadInt32();
            var info = Multiplayer.game.sync.buffer.FirstOrDefault(b => b.startTick == tick);

            Log.Message($"{info?.traces.Count} arbiter traces");
            File.WriteAllText("arbiter_traces.txt", info?.TracesToString(start, end) ?? "null");
        }
    }

    public class ClientSteamState : MpConnectionState
    {
        public ClientSteamState(IConnection connection) : base(connection)
        {
            //connection.Send(Packets.Client_SteamRequest);
        }

        [PacketHandler(Packets.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientJoining;
        }
    }

    public interface IConnectionStatusListener
    {
        void Connected();
        void Disconnected();
    }

    public static class ConnectionStatusListeners
    {
        private static IEnumerable<IConnectionStatusListener> All
        {
            get
            {
                if (Find.WindowStack != null)
                    foreach (var window in Find.WindowStack.Windows.ToList())
                        if (window is IConnectionStatusListener listener)
                            yield return listener;

                if (Multiplayer.Client?.StateObj is IConnectionStatusListener state)
                    yield return state;

                if (Multiplayer.session != null)
                    yield return Multiplayer.session;
            }
        }

        public static void TryNotifyAll_Connected()
        {
            foreach (var listener in All)
                try
                {
                    listener.Connected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
        }

        public static void TryNotifyAll_Disconnected()
        {
            foreach (var listener in All)
                try
                {
                    listener.Disconnected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
        }
    }
}