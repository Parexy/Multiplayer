#region

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#endregion

namespace Multiplayer.Common
{
    public class ServerJoiningState : MpConnectionState
    {
        public static readonly Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");

        public ServerJoiningState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.ClientProtocol)]
        public void HandleProtocol(ByteReader data)
        {
            int clientProtocol = data.ReadInt32();
            if (clientProtocol != MpVersion.Protocol)
            {
                Player.Disconnect(MpDisconnectReason.Protocol,
                    ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
                return;
            }

            connection.Send(Packets.ServerModList, Server.rwVersion, Server.modNames);
        }

        [PacketHandler(Packets.ClientDefs)]
        public void HandleDefs(ByteReader data)
        {
            int count = data.ReadInt32();
            if (count > 512)
            {
                Player.Disconnect(MpDisconnectReason.Generic);
                return;
            }

            ByteWriter response = new ByteWriter();
            bool disconnect = false;

            for (int i = 0; i < count; i++)
            {
                string defType = data.ReadString(128);
                int defCount = data.ReadInt32();
                int defHash = data.ReadInt32();

                DefCheckStatus status = DefCheckStatus.Ok;

                if (!Server.defInfos.TryGetValue(defType, out DefInfo info))
                    status = DefCheckStatus.NotFound;
                else if (info.count != defCount)
                    status = DefCheckStatus.CountDiff;
                else if (info.hash != defHash)
                    status = DefCheckStatus.HashDiff;

                if (status != DefCheckStatus.Ok)
                    disconnect = true;

                response.WriteByte((byte) status);
            }

            if (disconnect)
            {
                Player.Disconnect(MpDisconnectReason.Defs, response.ToArray());
                return;
            }

            connection.Send(Packets.ServerDefsOk, Server.settings.gameName, Player.id);
        }

        [PacketHandler(Packets.ClientUsername)]
        public void HandleClientUsername(ByteReader data)
        {
            if (connection.username != null && connection.username.Length != 0)
                return;

            string username = data.ReadString();

            if (username.Length < 3 || username.Length > 15)
            {
                Player.Disconnect(MpDisconnectReason.UsernameLength);
                return;
            }

            if (!Player.IsArbiter && !UsernamePattern.IsMatch(username))
            {
                Player.Disconnect(MpDisconnectReason.UsernameChars);
                return;
            }

            if (Server.GetPlayer(username) != null)
            {
                Player.Disconnect(MpDisconnectReason.UsernameAlreadyOnline);
                return;
            }

            connection.username = username;

            Server.SendNotification("MpPlayerConnected", Player.Username);
            Server.SendChat($"{Player.Username} has joined.");

            ByteWriter writer = new ByteWriter();
            writer.WriteByte((byte) PlayerListAction.Add);
            writer.WriteRaw(Player.SerializePlayerInfo());

            Server.SendToAll(Packets.ServerPlayerList, writer.ToArray());

            SendWorldData();
        }

        private void SendWorldData()
        {
            int factionId = MultiplayerServer.instance.coopFactionId;
            MultiplayerServer.instance.playerFactions[connection.username] = factionId;

            /*if (!MultiplayerServer.instance.playerFactions.TryGetValue(connection.Username, out int factionId))
            {
                factionId = MultiplayerServer.instance.nextUniqueId++;
                MultiplayerServer.instance.playerFactions[connection.Username] = factionId;

                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.SETUP_FACTION, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }*/

            if (Server.PlayingPlayers.Count(p => p.FactionId == factionId) == 1)
            {
                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.FactionOnline, ScheduledCommand.NoFaction,
                    ScheduledCommand.Global, extra);
            }

            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(factionId);
            writer.WriteInt32(MultiplayerServer.instance.gameTimer);
            writer.WritePrefixedBytes(MultiplayerServer.instance.savedGame);

            writer.WriteInt32(MultiplayerServer.instance.mapCmds.Count);

            foreach (KeyValuePair<int, List<byte[]>> kv in MultiplayerServer.instance.mapCmds)
            {
                int mapId = kv.Key;

                //MultiplayerServer.instance.SendCommand(CommandType.CreateMapFactionData, ScheduledCommand.NoFaction, mapId, ByteWriter.GetBytes(factionId));

                List<byte[]> mapCmds = kv.Value;

                writer.WriteInt32(mapId);

                writer.WriteInt32(mapCmds.Count);
                foreach (byte[] arr in mapCmds)
                    writer.WritePrefixedBytes(arr);
            }

            writer.WriteInt32(MultiplayerServer.instance.mapData.Count);

            foreach (KeyValuePair<int, byte[]> kv in MultiplayerServer.instance.mapData)
            {
                int mapId = kv.Key;
                byte[] mapData = kv.Value;

                writer.WriteInt32(mapId);
                writer.WritePrefixedBytes(mapData);
            }

            connection.State = ConnectionStateEnum.ServerPlaying;

            byte[] packetData = writer.ToArray();
            connection.SendFragmented(Packets.ServerWorldData, packetData);

            Player.SendPlayerList();

            MpLog.Log("World response sent: " + packetData.Length);
        }
    }

    public enum DefCheckStatus : byte
    {
        Ok,
        NotFound,
        CountDiff,
        HashDiff
    }

    public class ServerPlayingState : MpConnectionState
    {
        public const int MaxChatMsgLength = 128;

        public ServerPlayingState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.ClientWorldReady)]
        public void HandleWorldReady(ByteReader data)
        {
            Player.UpdateStatus(PlayerStatus.Playing);
        }

        [PacketHandler(Packets.ClientDesynced)]
        public void HandleDesynced(ByteReader data)
        {
            Player.UpdateStatus(PlayerStatus.Desynced);
        }

        [PacketHandler(Packets.ClientCommand)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType) data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extra = data.ReadPrefixedBytes(32767);

            // todo check if map id is valid for the player

            int factionId = MultiplayerServer.instance.playerFactions[connection.username];
            MultiplayerServer.instance.SendCommand(cmd, factionId, mapId, extra, Player);
        }

        [PacketHandler(Packets.ClientChat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            // todo handle max length
            if (msg.Length == 0) return;

            if (msg[0] == '/')
            {
                string cmd = msg.Substring(1);
                string[] parts = cmd.Split(' ');
                ChatCmdHandler handler = Server.GetCmdHandler(parts[0]);

                if (handler != null)
                {
                    if (handler.requiresHost && Player.Username != Server.hostUsername)
                        Player.SendChat("No permission");
                    else
                        handler.Handle(Player, parts.SubArray(1));
                }
                else
                {
                    Player.SendChat("Invalid command");
                }
            }
            else
            {
                Server.SendChat($"{connection.username}: {msg}");
            }
        }

        [PacketHandler(Packets.ClientAutosavedData)]
        [IsFragmented]
        public void HandleAutosavedData(ByteReader data)
        {
            bool arbiter = Server.ArbiterPlaying;
            if (arbiter && !Player.IsArbiter) return;
            if (!arbiter && Player.Username != Server.hostUsername) return;

            int maps = data.ReadInt32();
            for (int i = 0; i < maps; i++)
            {
                int mapId = data.ReadInt32();
                Server.mapData[mapId] = data.ReadPrefixedBytes();
            }

            Server.savedGame = data.ReadPrefixedBytes();

            if (Server.tmpMapCmds != null)
            {
                Server.mapCmds = Server.tmpMapCmds;
                Server.tmpMapCmds = null;
            }
        }

        [PacketHandler(Packets.ClientCursor)]
        public void HandleCursor(ByteReader data)
        {
            if (Player.lastCursorTick == Server.netTimer) return;

            ByteWriter writer = new ByteWriter();

            byte seq = data.ReadByte();
            byte map = data.ReadByte();

            writer.WriteInt32(Player.id);
            writer.WriteByte(seq);
            writer.WriteByte(map);

            if (map < byte.MaxValue)
            {
                byte icon = data.ReadByte();
                short x = data.ReadShort();
                short z = data.ReadShort();

                writer.WriteByte(icon);
                writer.WriteShort(x);
                writer.WriteShort(z);

                short dragX = data.ReadShort();
                writer.WriteShort(dragX);

                if (dragX != -1)
                {
                    short dragZ = data.ReadShort();
                    writer.WriteShort(dragZ);
                }
            }

            Player.lastCursorTick = Server.netTimer;

            Server.SendToAll(Packets.ServerCursor, writer.ToArray(), false, Player);
        }

        [PacketHandler(Packets.ClientSelected)]
        public void HandleSelected(ByteReader data)
        {
            bool reset = data.ReadBool();

            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(Player.id);
            writer.WriteBool(reset);
            writer.WritePrefixedInts(data.ReadPrefixedInts(100));
            writer.WritePrefixedInts(data.ReadPrefixedInts(100));

            Server.SendToAll(Packets.ServerSelected, writer.ToArray(), excluding: Player);
        }

        [PacketHandler(Packets.ClientIdBlockRequest)]
        public void HandleIdBlockRequest(ByteReader data)
        {
            int mapId = data.ReadInt32();

            if (mapId == ScheduledCommand.Global)
            {
                //IdBlock nextBlock = MultiplayerServer.instance.NextIdBlock();
                //MultiplayerServer.instance.SendCommand(CommandType.GlobalIdBlock, ScheduledCommand.NoFaction, ScheduledCommand.Global, nextBlock.Serialize());
            }
        }

        [PacketHandler(Packets.ClientKeepAlive)]
        public void HandleClientKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = data.ReadInt32();

            Player.ticksBehind = ticksBehind;

            // Latency already handled by LiteNetLib
            if (connection is MpNetConnection) return;

            if (MultiplayerServer.instance.keepAliveId == id)
                connection.Latency = (int) MultiplayerServer.instance.lastKeepAlive.ElapsedMilliseconds / 2;
            else
                connection.Latency = 2000;
        }

        [PacketHandler(Packets.ClientSyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            bool arbiter = Server.ArbiterPlaying;
            if (arbiter && !Player.IsArbiter) return;
            if (!arbiter && Player.Username != Server.hostUsername) return;

            byte[] raw = data.ReadRaw(data.Left);
            foreach (ServerPlayer p in Server.PlayingPlayers.Where(p => !p.IsArbiter))
                p.conn.SendFragmented(Packets.ServerSyncInfo, raw);
        }

        [PacketHandler(Packets.ClientPause)]
        public void HandlePause(ByteReader data)
        {
            bool pause = data.ReadBool();
            if (pause && Player.Username != Server.hostUsername) return;
            if (Server.paused == pause) return;

            Server.paused = pause;
            Server.SendToAll(Packets.ServerPause, new object[] {pause});
        }

        [PacketHandler(Packets.ClientDebug)]
        public void HandleDebug(ByteReader data)
        {
            if (!MpVersion.IsDebug) return;

            Server.PlayingPlayers.FirstOrDefault(p => p.IsArbiter)
                ?.SendPacket(Packets.ServerDebug, data.ReadRaw(data.Left));
        }
    }

    public enum PlayerListAction : byte
    {
        List,
        Add,
        Remove,
        Latencies,
        Status
    }

    // Unused
    public class ServerSteamState : MpConnectionState
    {
        public ServerSteamState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.ClientSteamRequest)]
        public void HandleSteamRequest(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ServerJoining;
            connection.Send(Packets.ServerSteamAccept);
        }
    }
}