#region

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Multiplayer.Common;
using Steamworks;
using UnityEngine;

#endregion

namespace Multiplayer.Client
{
    public static class SteamIntegration
    {
        public const string SteamConnectStart = " -mpserver=";
        private static Callback<P2PSessionRequest_t> sessionReq;
        private static Callback<P2PSessionConnectFail_t> p2pFail;
        private static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        private static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        private static Callback<PersonaStateChange_t> personaChange;

        public static AppId_t RimWorldAppId;

        private static readonly Stopwatch lastSteamUpdate = Stopwatch.StartNew();
        private static bool lastSteam;

        public static void InitCallbacks()
        {
            RimWorldAppId = SteamUtils.GetAppID();

            sessionReq = Callback<P2PSessionRequest_t>.Create(req =>
            {
                var session = Multiplayer.session;
                if (session?.localSettings != null && session.localSettings.steam &&
                    !session.pendingSteam.Contains(req.m_steamIDRemote))
                {
                    if (MultiplayerMod.settings.autoAcceptSteam)
                        SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote);
                    else
                        session.pendingSteam.Add(req.m_steamIDRemote);

                    session.knownUsers.Add(req.m_steamIDRemote);
                    session.NotifyChat();

                    SteamFriends.RequestUserInformation(req.m_steamIDRemote, true);
                }
            });

            friendRchpUpdate = Callback<FriendRichPresenceUpdate_t>.Create(update => { });

            gameJoinReq = Callback<GameRichPresenceJoinRequested_t>.Create(req => { });

            personaChange = Callback<PersonaStateChange_t>.Create(change => { });

            p2pFail = Callback<P2PSessionConnectFail_t>.Create(fail =>
            {
                var session = Multiplayer.session;
                if (session == null) return;

                var remoteId = fail.m_steamIDRemote;
                var error = (EP2PSessionError) fail.m_eP2PSessionError;

                if (Multiplayer.Client is SteamBaseConn clientConn && clientConn.remoteId == remoteId)
                    clientConn.OnError(error);

                var server = Multiplayer.LocalServer;
                if (server == null) return;

                server.Enqueue(() =>
                {
                    var conn = server.players.Select(p => p.conn).OfType<SteamBaseConn>()
                        .FirstOrDefault(c => c.remoteId == remoteId);
                    if (conn != null)
                        conn.OnError(error);
                });
            });
        }

        public static IEnumerable<SteamPacket> ReadPackets()
        {
            while (SteamNetworking.IsP2PPacketAvailable(out var size, 0))
            {
                var data = new byte[size];

                if (!SteamNetworking.ReadP2PPacket(data, size, out var sizeRead, out var remote, 0)) continue;
                if (data.Length <= 0) continue;

                var reader = new ByteReader(data);
                var info = reader.ReadByte();
                var joinPacket = (info & 1) > 0;
                var reliable = (info & 2) > 0;

                yield return new SteamPacket()
                    {remote = remote, data = reader, joinPacket = joinPacket, reliable = reliable};
            }
        }

        public static void ClearChannel(int channel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out var size, channel))
                SteamNetworking.ReadP2PPacket(new byte[size], size, out var sizeRead, out var remote, channel);
        }

        public static void ServerSteamNetTick(MultiplayerServer server)
        {
            foreach (var packet in ReadPackets())
            {
                if (packet.joinPacket)
                    ClearChannel(0);

                var player =
                    server.players.FirstOrDefault(p => p.conn is SteamBaseConn conn && conn.remoteId == packet.remote);

                if (packet.joinPacket && player == null)
                {
                    IConnection conn = new SteamServerConn(packet.remote);
                    conn.State = ConnectionStateEnum.ServerJoining;
                    player = server.OnConnected(conn);
                    player.type = PlayerType.Steam;

                    player.steamId = (ulong) packet.remote;
                    player.steamPersonaName = SteamFriends.GetFriendPersonaName(packet.remote);
                    if (player.steamPersonaName.Length == 0)
                        player.steamPersonaName = "[unknown]";

                    conn.Send(Packets.Server_SteamAccept);
                }

                if (!packet.joinPacket && player != null) player.HandleReceive(packet.data, packet.reliable);
            }
        }

        public static void UpdateRichPresence()
        {
            if (lastSteamUpdate.ElapsedMilliseconds < 1000) return;

            var steam = Multiplayer.session?.localSettings?.steam ?? false;

            if (steam != lastSteam)
            {
                if (steam)
                    SteamFriends.SetRichPresence("connect", $"{SteamConnectStart}{SteamUser.GetSteamID()}");
                else
                    SteamFriends.SetRichPresence("connect", null);

                lastSteam = steam;
            }

            lastSteamUpdate.Restart();
        }
    }

    public struct SteamPacket
    {
        public CSteamID remote;
        public ByteReader data;
        public bool joinPacket;
        public bool reliable;
    }

    public static class SteamImages
    {
        public static Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        // Remember to flip it
        public static Texture2D GetTexture(int id)
        {
            if (cache.TryGetValue(id, out var tex))
                return tex;

            if (!SteamUtils.GetImageSize(id, out var width, out var height))
            {
                cache[id] = null;
                return null;
            }

            var sizeInBytes = width * height * 4;
            var data = new byte[sizeInBytes];

            if (!SteamUtils.GetImageRGBA(id, data, (int) sizeInBytes))
            {
                cache[id] = null;
                return null;
            }

            tex = new Texture2D((int) width, (int) height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(data);
            tex.Apply();

            cache[id] = tex;

            return tex;
        }
    }
}