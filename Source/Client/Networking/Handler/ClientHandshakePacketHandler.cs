using System.Collections.Generic;
using System.IO;
using System.Xml;
using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;
using Verse;
using Verse.Profile;

namespace Multiplayer.Client.Networking.Handler
{
    public class ClientHandshakePacketHandler : MpPacketHandler
    {
        public JoiningState state = JoiningState.Connected;

        public ClientHandshakePacketHandler(BaseMultiplayerConnection connection) : base(connection)
        {
            connection.Send(Packet.Client_Protocol, MpVersion.Protocol);

            ConnectionStatusListeners.TryNotifyAll_Connected();
        }

        [HandlesPacket(Packet.Server_ModList)]
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

            connection.Send(Packet.Client_Defs, response.ToArray());
        }

        [HandlesPacket(Packet.Server_DefsOK)]
        public void HandleDefsOK(ByteReader data)
        {
            Multiplayer.session.gameName = data.ReadString();
            Multiplayer.session.playerId = data.ReadInt32();

            connection.Send(Packet.Client_Username, Multiplayer.username);

            state = JoiningState.Downloading;
        }

        [HandlesPacket(Packet.Server_WorldData)]
        [IsFragmented]
        public void HandleWorldData(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientPlaying;
            Log.Message("Game data size: " + data.Length);

            int factionId = data.ReadInt32();
            Multiplayer.session.myFactionId = factionId;

            int tickUntil = data.ReadInt32();

            byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedGameData = worldData;

            List<int> mapsToLoad = new List<int>();

            int mapCmdsCount = data.ReadInt32();
            for (int i = 0; i < mapCmdsCount; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (int j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                OnMainThread.cachedMapCmds[mapId] = mapCmds;
            }

            int mapDataCount = data.ReadInt32();
            for (int i = 0; i < mapDataCount; i++)
            {
                int mapId = data.ReadInt32();
                byte[] rawMapData = data.ReadPrefixedBytes();

                byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
                OnMainThread.cachedMapData[mapId] = mapData;
                mapsToLoad.Add(mapId);
            }

            TickPatch.tickUntil = tickUntil;

            TickPatch.SkipTo(
                tickUntilCaughtUp: true,
                onFinish: () => Multiplayer.Client.Send(Packet.Client_WorldReady),
                cancelButtonKey: "Quit",
                onCancel: GenScene.GoToMainMenu
            );

            ReloadGame(mapsToLoad);
        }

        private static XmlDocument GetGameDocument(List<int> mapsToLoad)
        {
            XmlDocument gameDoc = ScribeUtil.LoadDocument(OnMainThread.cachedGameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (int map in mapsToLoad)
            {
                using (XmlReader reader = XmlReader.Create(new MemoryStream(OnMainThread.cachedMapData[map])))
                {
                    XmlNode mapNode = gameDoc.ReadNode(reader);
                    gameNode["maps"].AppendChild(mapNode);

                    if (gameNode["currentMapIndex"] == null)
                        gameNode.AddNode("currentMapIndex", map.ToString());
                }
            }

            return gameDoc;
        }

        public static void ReloadGame(List<int> mapsToLoad, bool async = true)
        {
            LoadPatch.gameToLoad = GetGameDocument(mapsToLoad);
            TickPatch.replayTimeSpeed = TimeSpeed.Paused;

            if (async)
            {
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
            }
            else
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.LoadInMainThread(LoadPatch.gameToLoad);
                    PostLoad();
                }, "MpLoading", false, null);
            }
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

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            // Map cmds are added in MapAsyncTimeComp.FinalizeInit
        }
    }
}