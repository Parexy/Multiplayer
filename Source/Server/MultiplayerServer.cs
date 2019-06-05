using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using LiteNetLib;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Common.Networking.Connection;
using Multiplayer.Common.Networking.Handler;
using Multiplayer.Server.Networking.Handler;
using Verse;

namespace Multiplayer.Server
{
    public class MultiplayerServer
    {
        static MultiplayerServer()
        {
            MpPacketHandler.SetPacketHandlerForState(ConnectionStateEnum.ServerJoining, typeof(ServerHandshakePacketHandler));
            MpPacketHandler.SetPacketHandlerForState(ConnectionStateEnum.ServerPlaying, typeof(ServerGameplayPacketHandler));
        }

        public static MultiplayerServer instance;

        public const int DefaultPort = 30502;

        public int coopFactionId;
        public byte[] savedGame; // Compressed game save
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>(); // Map id to compressed map data

        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>(); // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> tmpMapCmds;

        // todo remove entries
        public Dictionary<string, int> playerFactions = new Dictionary<string, int>(); // Username to faction id

        public List<ServerPlayer> players = new List<ServerPlayer>();
        public IEnumerable<ServerPlayer> PlayingPlayers => players.Where(p => p.IsPlaying);

        public string hostUsername;
        public int gameTimer;
        public bool paused;
        public ActionQueue queue = new ActionQueue();
        public ServerSettings settings;
        public bool debugMode;

        public volatile bool running = true;

        private Dictionary<string, ChatCmdHandler> chatCmds = new Dictionary<string, ChatCmdHandler>();
        public HashSet<int> debugOnlySyncCmds = new HashSet<int>();
        public HashSet<int> hostOnlySyncCmds = new HashSet<int>();

        public int keepAliveId;
        public Stopwatch lastKeepAlive = Stopwatch.StartNew();

        private NetManager netManager;
        private NetManager lanManager;
        private NetManager arbiter;

        public int nextUniqueId; // currently unused

        public string rwVersion;
        public string[] modNames;
        public Dictionary<string, DefDatabaseInfo> defInfos;

        public int NetPort => netManager.LocalPort;
        public int LanPort => lanManager.LocalPort;
        public int ArbiterPort => arbiter.LocalPort;

        public bool ArbiterPlaying => PlayingPlayers.Any(p => p.IsArbiter && p.status == ServerPlayer.Status.Playing);

        public event Action<MultiplayerServer> NetTick;

        private float autosaveCountdown;

        public MultiplayerServer(ServerSettings settings)
        {
            this.settings = settings;

            RegisterChatCmd("autosave", new ChatCmdAutosave());
            RegisterChatCmd("kick", new ChatCmdKick());

            if (settings.bindAddress != null)
                netManager = new NetManager(new ServerNetListener(this, false));

            if (settings.lanAddress != null)
                lanManager = new NetManager(new ServerNetListener(this, false));

            autosaveCountdown = settings.autosaveInterval * 2500 * 24;
        }

        public bool? StartListeningNet()
        {
            return netManager?.Start(IPAddress.Parse(settings.bindAddress), IPAddress.IPv6Any, settings.bindPort);
        }

        public bool? StartListeningLan()
        {
            return lanManager?.Start(IPAddress.Parse(settings.lanAddress), IPAddress.IPv6Any, 0);
        }

        public void SetupArbiterConnection()
        {
            arbiter = new NetManager(new ServerNetListener(this, true));
            arbiter.Start(IPAddress.Loopback, IPAddress.IPv6Any, 0);
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                double elapsed = time.ElapsedMillisDouble();
                time.Restart();
                lag += elapsed;

                while (lag >= timePerTick)
                {
                    TickNet();
                    if (!paused && PlayingPlayers.Any(p => !p.IsArbiter && p.status == ServerPlayer.Status.Playing))
                        Tick();
                    lag -= timePerTick;
                }

                Thread.Sleep(10);
            }

            Stop();
        }

        private void Stop()
        {
            foreach (var player in players)
                player.conn.Close(MpDisconnectReason.ServerClosed);

            netManager?.Stop();
            lanManager?.Stop();
            arbiter?.Stop();

            instance = null;
        }

        public int netTimer;

        public void TickNet()
        {
            netManager?.PollEvents();
            lanManager?.PollEvents();
            arbiter?.PollEvents();

            NetTick?.Invoke(this);

            queue.RunQueue();

            if (lanManager != null && netTimer % 60 == 0)
                lanManager.SendDiscoveryRequest(Encoding.UTF8.GetBytes("mp-server"), 5100);

            netTimer++;

            if (netTimer % 180 == 0)
            {
                SendLatencies();

                keepAliveId++;
                SendToAll(Packet.Server_KeepAlive, new object[] { keepAliveId });
                lastKeepAlive.Restart();
            }
        }

        public void Tick()
        {
            if (gameTimer % 3 == 0)
                SendToAll(Packet.Server_TimeControl, new object[] { gameTimer });

            gameTimer++;

            if (settings.autosaveInterval <= 0)
                return;

            var curSpeed = Client.Multiplayer.WorldComp.TimeSpeed;

            autosaveCountdown -= (curSpeed == Verse.TimeSpeed.Paused && !Client.MultiplayerMod.settings.pauseAutosaveCounter) 
                ? 1 : Client.Multiplayer.WorldComp.TickRateMultiplier(curSpeed);

            if (autosaveCountdown <= 0)
                DoAutosave();
        }

        private void SendLatencies()
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Latencies);

            writer.WriteInt32(PlayingPlayers.Count());
            foreach (var player in PlayingPlayers)
            {
                writer.WriteInt32(player.Latency);
                writer.WriteInt32(player.ticksBehind);
            }

            SendToAll(Packet.Server_PlayerList, writer.ToArray());
        }

        public bool DoAutosave()
        {
            if (tmpMapCmds != null)
                return false;

            if (settings.pauseOnAutosave)
                SendCommand(CommandType.WorldTimeSpeed, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[] { (byte)Verse.TimeSpeed.Paused });

            SendCommand(CommandType.Autosave, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[0]);
            tmpMapCmds = new Dictionary<int, List<byte[]>>();

            SendChat("Autosaving...");

            autosaveCountdown = settings.autosaveInterval * 2500 * 24;
            return true;
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        private int nextPlayerId;

        public ServerPlayer OnConnected(BaseMultiplayerConnection conn)
        {
            if (conn.serverPlayer != null)
                MpLog.Error($"Connection {conn} already has a server player");

            conn.serverPlayer = new ServerPlayer(nextPlayerId++, conn);
            players.Add(conn.serverPlayer);
            MpLog.Log($"New connection: {conn}");

            return conn.serverPlayer;
        }

        public void OnDisconnected(BaseMultiplayerConnection conn, MpDisconnectReason reason)
        {
            if (conn.State == ConnectionStateEnum.Disconnected) return;

            ServerPlayer player = conn.serverPlayer;
            players.Remove(player);

            if (player.IsPlaying)
            {
                if (!players.Any(p => p.FactionId == player.FactionId))
                {
                    byte[] data = ByteWriter.GetBytes(player.FactionId);
                    SendCommand(CommandType.FactionOffline, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                }

                SendNotification("MpPlayerDisconnected", conn.username);
                SendChat($"{conn.username} has left.");

                SendToAll(Packet.Server_PlayerList, new object[] { (byte)PlayerListAction.Remove, player.id });
            }

            conn.State = ConnectionStateEnum.Disconnected;

            MpLog.Log($"Disconnected ({reason}): {conn}");
        }

        public void SendToAll(Packet id)
        {
            SendToAll(id, new byte[0]);
        }

        public void SendToAll(Packet id, object[] data)
        {
            SendToAll(id, ByteWriter.GetBytes(data));
        }

        public void SendToAll(Packet id, byte[] data, bool reliable = true, ServerPlayer excluding = null)
        {
            foreach (ServerPlayer player in PlayingPlayers)
                if (player != excluding)
                    player.conn.Send(id, data, reliable);
        }

        public ServerPlayer GetPlayer(string username)
        {
            return players.Find(player => player.Username == username);
        }

        public IdBlock NextIdBlock(int blockSize = 30000)
        {
            int blockStart = nextUniqueId;
            nextUniqueId = nextUniqueId + blockSize;
            MpLog.Log($"New id block {blockStart} of size {blockSize}");

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int factionId, int mapId, byte[] data, ServerPlayer sourcePlayer = null)
        {
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && debugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));

                if (!debugMode && debugCmd)
                    return;

                bool hostOnly = cmd == CommandType.Sync && hostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (!sourcePlayer.IsHost && hostOnly)
                    return;
            }

            byte[] toSave = new ScheduledCommand(cmd, gameTimer, factionId, mapId, data).Serialize();

            // todo cull target players if not global
            mapCmds.GetOrAddNew(mapId).Add(toSave);
            tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (var player in PlayingPlayers)
            {
                player.conn.Send(
                    Packet.Server_Command,
                    sourcePlayer == player ? toSendSource : toSend
                );
            }
        }

        public void SendChat(string msg)
        {
            SendToAll(Packet.Server_Chat, new[] { msg });
        }

        public void SendNotification(string key, params string[] args)
        {
            SendToAll(Packet.Server_Notification, new object[] { key, args });
        }

        public void RegisterChatCmd(string cmdName, ChatCmdHandler handler)
        {
            chatCmds[cmdName] = handler;
        }

        public ChatCmdHandler GetCmdHandler(string cmdName)
        {
            chatCmds.TryGetValue(cmdName, out ChatCmdHandler handler);
            return handler;
        }
    }
}
