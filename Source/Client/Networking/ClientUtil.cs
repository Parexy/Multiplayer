using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Windows;
using Multiplayer.Common;
using Multiplayer.Server.Networking;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Networking
{
    public static class ClientUtil
    {
        /// <summary>
        /// Atttempts to connect directly to the provided server address and port
        /// </summary>
        /// <param name="address">The IP address of the server to connect to</param>
        /// <param name="port">The port to connect on</param>
        public static void TryConnectDirect(string address, int port)
        {
            Multiplayer.session = new MultiplayerSession();
            NetManager netClient = new NetManager(new MpClientNetListener());

            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address, port, "");
        }

        /// <summary>
        /// Initializes a <see cref="MultiplayerServer"/> running on the local machine, and a pair of LocalhostConnections (Server2Client and Client2Server) to simulate communication between them
        /// </summary>
        /// <param name="settings">The configuration options for the new server</param>
        /// <param name="fromReplay">The replay to host from</param>
        /// <param name="watchMode">Whether to launch the server in watch only/spectator mode</param>
        /// <param name="debugMode">Whether to launch the server as a debug build</param>
        public static void HostServer(ServerSettings settings, bool fromReplay, bool watchMode = false, bool debugMode = false)
        {
            Log.Message($"Starting the server");

            var session = Multiplayer.session = new MultiplayerSession();
            session.myFactionId = Faction.OfPlayer.loadID;
            session.localSettings = settings;
            session.gameName = settings.gameName;

            var localServer = new MultiplayerServer(settings);

            if (watchMode)
            {
                localServer.savedGame = GZipStream.CompressBuffer(OnMainThread.cachedGameData);
                localServer.mapData = OnMainThread.cachedMapData.ToDictionary(kv => kv.Key, kv => GZipStream.CompressBuffer(kv.Value));
                localServer.mapCmds = OnMainThread.cachedMapCmds.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Serialize()).ToList());
            }
            else
            {
                OnMainThread.ClearCaches();
            }

            localServer.debugMode = debugMode;
            localServer.debugOnlySyncCmds = new HashSet<int>(Sync.Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId));
            localServer.hostOnlySyncCmds = new HashSet<int>(Sync.Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId));
            localServer.hostUsername = Multiplayer.username;
            localServer.coopFactionId = Faction.OfPlayer.loadID;

            localServer.rwVersion = session.mods.remoteRwVersion = VersionControl.CurrentVersionString;
            localServer.modNames = session.mods.remoteModNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToArray();
            localServer.defInfos = session.mods.defInfo = Multiplayer.localDefInfos;

            if (settings.steam)
                localServer.NetTick += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
                localServer.gameTimer = TickPatch.Timer;

            MultiplayerServer.instance = localServer;
            session.localServer = localServer;

            if (!fromReplay)
                SetupGame();

            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("Wiki on desyncs:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://github.com/Zetrith/Multiplayer/wiki/Desyncs"), false);

            if (watchMode)
            {
                StartServerThread();
            }
            else
            {
                var timeSpeed = Prefs.data.pauseOnLoad ? TimeSpeed.Paused : TimeSpeed.Normal;

                Multiplayer.WorldComp.TimeSpeed = timeSpeed;
                foreach (var map in Find.Maps)
                    map.AsyncTime().TimeSpeed = timeSpeed;

                Multiplayer.WorldComp.debugMode = debugMode;

                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.CacheGameData(SaveLoad.SaveAndReload());
                    SaveLoad.SendCurrentGameData(false);

                    StartServerThread();
                }, "MpSaving", false, null);
            }

            void StartServerThread()
            {
                var netStarted = localServer.StartListeningNet();
                var lanStarted = localServer.StartListeningLan();

                string text = "Server started.";

                if (netStarted != null)
                    text += (netStarted.Value ? $" Direct at {settings.bindAddress}:{localServer.NetPort}." : " Couldn't bind direct.");

                if (lanStarted != null)
                    text += (lanStarted.Value ? $" LAN at {settings.lanAddress}:{localServer.LanPort}." : " Couldn't bind LAN.");

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                Messages.Message(text, MessageTypeDefOf.SilentInput, false);
                Log.Message(text);
            }
        }

        /// <summary>
        /// Initializes the <see cref="Multiplayer.game"/> field, assignes unique IDs to policies, sets up the dummy faction as well as the player's faction, and configures the
        /// <see cref="MapAsyncTimeComp"/> and <see cref="MultiplayerWorldComp"/>
        /// </summary>
        private static void SetupGame()
        {
            MultiplayerWorldComp comp = new MultiplayerWorldComp(Find.World);
            Faction dummyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == -1);

            if (dummyFaction == null)
            {
                dummyFaction = new Faction() { loadID = -1, def = Multiplayer.DummyFactionDef };

                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                    dummyFaction.TryMakeInitialRelationsWith(other);

                Find.FactionManager.Add(dummyFaction);

                comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);
            }

            dummyFaction.Name = "Multiplayer dummy faction";
            dummyFaction.def = Multiplayer.DummyFactionDef;

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            comp.globalIdBlock = new IdBlock(GetMaxUniqueId(), 1_000_000_000);

            foreach (FactionWorldData data in comp.factionData.Values)
            {
                foreach (DrugPolicy p in data.drugPolicyDatabase.policies)
                    p.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (Outfit o in data.outfitDatabase.outfits)
                    o.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (FoodRestriction o in data.foodRestrictionDatabase.foodRestrictions)
                    o.id = Multiplayer.GlobalIdBlock.NextId();
            }

            foreach (Map map in Find.Maps)
            {
                //mapComp.mapIdBlock = localServer.NextIdBlock();

                BeforeMapGeneration.SetupMap(map);

                MapAsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
            }
        }

        /// <summary>
        /// Initializes the two LocalhostConnections and hooks them up with each other, starts the arbiter if enabled, then sets 
        /// </summary>
        private static void SetupLocalClient()
        {
            if (Multiplayer.session.localSettings.arbiter)
                StartArbiter();

            ClientToServerLocalhostConnection client2Server = new ClientToServerLocalhostConnection(Multiplayer.username);
            ServerToClientLocalhostConnection server2Client = new ServerToClientLocalhostConnection(Multiplayer.username);

            server2Client.clientSide = client2Server;
            client2Server.serverSide = server2Client;

            client2Server.State = ConnectionStateEnum.ClientPlaying;
            server2Client.State = ConnectionStateEnum.ServerPlaying;

            var serverPlayer = Multiplayer.LocalServer.OnConnected(server2Client);
            serverPlayer.status = PlayerStatus.Playing;
            serverPlayer.SendPlayerList();

            Multiplayer.session.client = client2Server;
            Multiplayer.session.ReapplyPrefs();
        }

        /// <summary>
        /// Spawns a sub-process in unity's batchmode for the arbiter and instructs it to connect to the local server. 
        /// </summary>
        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...", false);

            Multiplayer.LocalServer.SetupArbiterConnection();

            string args = $"-batchmode -nographics -arbiter -logfile arbiter_log.txt -connect=127.0.0.1:{Multiplayer.LocalServer.ArbiterPort}";

            if(GenCommandLine.TryGetCommandLineArg("savedatafolder", out string saveDataFolder))
                args += $" -savedatafolder=\"{saveDataFolder}\"";

            Multiplayer.session.arbiter = Process.Start(
                Process.GetCurrentProcess().MainModule.FileName,
                args
            );
        }

        /// <summary>
        /// Finds which of the fields from the <see cref="UniqueIDsManager"/> has the highest value and returns that.
        /// </summary>
        /// <returns>The max ID currently in use by any game object</returns>
        private static int GetMaxUniqueId()
        {
            return typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => (int)f.GetValue(Find.UniqueIDsManager))
                .Max();
        }
    }
}