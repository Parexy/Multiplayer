#region

extern alias zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

#endregion

namespace Multiplayer.Client
{
    public class Replay
    {
        public ReplayInfo info;

        private Replay(FileInfo file)
        {
            File = file;
        }

        public FileInfo File { get; }

        public ZipFile ZipFile => new ZipFile(File.FullName);

        public void WriteCurrentData()
        {
            string sectionId = info.sections.Count.ToString("D3");

            using (ZipFile zip = ZipFile)
            {
                foreach (KeyValuePair<int, byte[]> kv in OnMainThread.cachedMapData)
                    zip.AddEntry($"maps/{sectionId}_{kv.Key}_save", kv.Value);

                foreach (KeyValuePair<int, List<ScheduledCommand>> kv in OnMainThread.cachedMapCmds)
                    if (kv.Key >= 0)
                        zip.AddEntry($"maps/{sectionId}_{kv.Key}_cmds", SerializeCmds(kv.Value));

                if (OnMainThread.cachedMapCmds.TryGetValue(ScheduledCommand.Global,
                    out List<ScheduledCommand> worldCmds))
                    zip.AddEntry($"world/{sectionId}_cmds", SerializeCmds(worldCmds));

                zip.AddEntry($"world/{sectionId}_save", OnMainThread.cachedGameData);
                zip.Save();
            }

            info.sections.Add(new ReplaySection(OnMainThread.cachedAtTime, TickPatch.Timer));
            WriteInfo();
        }

        public static byte[] SerializeCmds(List<ScheduledCommand> cmds)
        {
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(cmds.Count);
            foreach (ScheduledCommand cmd in cmds)
                writer.WritePrefixedBytes(cmd.Serialize());

            return writer.ToArray();
        }

        public static List<ScheduledCommand> DeserializeCmds(byte[] data)
        {
            ByteReader reader = new ByteReader(data);

            int count = reader.ReadInt32();
            List<ScheduledCommand> result = new List<ScheduledCommand>(count);
            for (int i = 0; i < count; i++)
                result.Add(ScheduledCommand.Deserialize(new ByteReader(reader.ReadPrefixedBytes())));

            return result;
        }

        public void WriteInfo()
        {
            using (ZipFile zip = ZipFile)
            {
                zip.UpdateEntry("info", DirectXmlSaver.XElementFromObject(info, typeof(ReplayInfo)).ToString());
                zip.Save();
            }
        }

        public bool LoadInfo()
        {
            using (ZipFile zip = ZipFile)
            {
                ZipEntry infoFile = zip["info"];
                if (infoFile == null) return false;

                XmlDocument doc = ScribeUtil.LoadDocument(infoFile.GetBytes());
                info = DirectXmlToObject.ObjectFromXml<ReplayInfo>(doc.DocumentElement, true);
            }

            return true;
        }

        public void LoadCurrentData(int sectionId)
        {
            string sectionIdStr = sectionId.ToString("D3");

            using (ZipFile zip = ZipFile)
            {
                foreach (ZipEntry mapCmds in zip.SelectEntries($"name = maps/{sectionIdStr}_*_cmds"))
                {
                    int mapId = int.Parse(mapCmds.FileName.Split('_')[1]);
                    OnMainThread.cachedMapCmds[mapId] = DeserializeCmds(mapCmds.GetBytes());
                }

                foreach (ZipEntry mapSave in zip.SelectEntries($"name = maps/{sectionIdStr}_*_save"))
                {
                    int mapId = int.Parse(mapSave.FileName.Split('_')[1]);
                    OnMainThread.cachedMapData[mapId] = mapSave.GetBytes();
                }

                ZipEntry worldCmds = zip[$"world/{sectionIdStr}_cmds"];
                if (worldCmds != null)
                    OnMainThread.cachedMapCmds[ScheduledCommand.Global] = DeserializeCmds(worldCmds.GetBytes());

                OnMainThread.cachedGameData = zip[$"world/{sectionIdStr}_save"].GetBytes();
            }
        }

        public static FileInfo ReplayFile(string fileName, string folder = null)
        {
            return new FileInfo(Path.Combine(folder ?? Multiplayer.ReplaysDir, $"{fileName}.zip"));
        }

        public static Replay ForLoading(string fileName)
        {
            return ForLoading(ReplayFile(fileName));
        }

        public static Replay ForLoading(FileInfo file)
        {
            return new Replay(file);
        }

        public static Replay ForSaving(string fileName)
        {
            return ForSaving(ReplayFile(fileName));
        }

        public static Replay ForSaving(FileInfo file)
        {
            Replay replay = new Replay(file)
            {
                info = new ReplayInfo
                {
                    name = Multiplayer.session.gameName,
                    playerFaction = Multiplayer.session.myFactionId,
                    protocol = MpVersion.Protocol,
                    rwVersion = VersionControl.CurrentVersionStringWithRev,
                    modIds = LoadedModManager.RunningModsListForReading.Select(m => m.Identifier).ToList(),
                    modNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToList(),
                    modAssemblyHashes = Multiplayer.enabledModAssemblyHashes.Select(h => h.assemblyHash).ToList()
                }
            };

            return replay;
        }

        public static void LoadReplay(FileInfo file, bool toEnd = false, Action after = null, Action cancel = null,
            string simTextKey = null)
        {
            MultiplayerSession session = Multiplayer.session = new MultiplayerSession();
            session.client = new ReplayConnection();
            session.client.State = ConnectionStateEnum.ClientPlaying;
            session.replay = true;

            Replay replay = ForLoading(file);
            replay.LoadInfo();

            int sectionIndex = toEnd ? replay.info.sections.Count - 1 : 0;
            replay.LoadCurrentData(sectionIndex);

            // todo ensure everything is read correctly

            session.myFactionId = replay.info.playerFaction;
            session.replayTimerStart = replay.info.sections[sectionIndex].start;

            int tickUntil = replay.info.sections[sectionIndex].end;
            session.replayTimerEnd = tickUntil;
            TickPatch.tickUntil = tickUntil;

            TickPatch.SkipTo(toEnd ? tickUntil : session.replayTimerStart, onFinish: after, onCancel: cancel,
                simTextKey: simTextKey);

            ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList());
        }
    }

    public class ReplayInfo
    {
        public List<ReplayEvent> events = new List<ReplayEvent>();
        public List<int> modAssemblyHashes;
        public List<string> modIds;
        public List<string> modNames;
        public string name;
        public int playerFaction;
        public int protocol;

        public string rwVersion;

        public List<ReplaySection> sections = new List<ReplaySection>();
    }

    public class ReplaySection
    {
        public int end;
        public int start;

        public ReplaySection()
        {
        }

        public ReplaySection(int start, int end)
        {
            this.start = start;
            this.end = end;
        }
    }

    public class ReplayEvent
    {
        public Color color;
        public string name;
        public int time;
    }

    public class ReplayConnection : IConnection
    {
        protected override void SendRaw(byte[] raw, bool reliable)
        {
        }

        public override void HandleReceive(ByteReader data, bool reliable)
        {
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }
    }
}