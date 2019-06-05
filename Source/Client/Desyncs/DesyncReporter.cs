extern alias zip;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Harmony;
using Multiplayer.Client.Windows;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client.Desyncs
{
    public class DesyncReporter
    {
        public static ClientSyncOpinion oldOpinion;
        public static ClientSyncOpinion newOpinion;
        public static DesyncedWindow window;
        public static string remoteStacks;
        private static ZipFile desyncReport;

        public static void SaveLocalAndPromptUpload()
        {
            if (oldOpinion == null || newOpinion == null) return;

            window.resizeLater = true;
            window.resizeLaterRect = new Rect(window.windowRect) {height = 150};
            window.resizeLaterRect.yMin -= 22;

            window.dataObtained = true;

            //Identify which of the two sync infos is local, and which is the remote.
            var local = oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;
            var remote = !oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;

            try
            {
                //Get the filename of the next desync file to create.
                var desyncFilePath = FindFileNameForNextDesyncFile();

                //Initialize the Replay object.
                var replay = Replay.ForSaving(Replay.ReplayFile(desyncFilePath, Multiplayer.DesyncsDir));

                //Write the universal replay data (i.e. world and map folders, and the info file) so this desync can be reviewed as a standard replay.
                replay.WriteCurrentData();

                //Dump our current game object.
                var savedGame = ScribeUtil.WriteExposable(Current.Game, "game", true, ScribeMetaHeaderUtility.WriteMetaHeader);

                desyncReport = new ZipFile();
                using (var zip = replay.ZipFile)
                {
                    //Write the local sync data
                    var syncLocal = local.Serialize();
                    zip.AddEntry("sync_local", syncLocal);
                    desyncReport.AddEntry("sync_local", syncLocal);

                    //Write the remote sync data
                    var syncRemote = remote.Serialize();
                    zip.AddEntry("sync_remote", syncRemote);
                    desyncReport.AddEntry("sync_remote", syncRemote);

                    //Dump the entire save file to the zip.
                    zip.AddEntry("game_snapshot", savedGame);
//                    desyncReport.AddEntry("game_snapshot", savedGame); //This ends up being about 15MB, we really don't want that. 

                    //Add local stack traces
                    zip.AddEntry("local_stacks", GetDesyncStackTraces(local, remote, out _));
                    desyncReport.AddEntry("local_stacks", GetDesyncStackTraces(local, remote, out _));

                    //Add remote's stack traces
                    zip.AddEntry("remote_stacks", remoteStacks);
                    desyncReport.AddEntry("remote_stacks", remoteStacks);

                    //Prepare the desync info
                    var desyncInfo = new StringBuilder();

                    desyncInfo.AppendLine("###Tick Data###")
                        .AppendLine($"Arbiter Connected And Playing|||{Multiplayer.session.ArbiterPlaying}")
                        .AppendLine($"Last Valid Tick - Local|||{Multiplayer.game.sync.lastValidTick}")
                        .AppendLine($"Arbiter Present on Last Tick|||{Multiplayer.game.sync.arbiterWasPlayingOnLastValidTick}")
                        .AppendLine("\n###Version Data###")
                        .AppendLine($"Multiplayer Mod Version|||{MpVersion.Version}")
                        .AppendLine($"Rimworld Version and Rev|||{VersionControl.CurrentVersionStringWithRev}")
                        .AppendLine("\n###Debug Options###")
                        .AppendLine($"Multiplayer Debug Build - Client|||{MpVersion.IsDebug}")
                        .AppendLine($"Multiplayer Debug Build - Host|||{Multiplayer.WorldComp.debugMode}")
                        .AppendLine($"Rimworld Developer Mode - Client|||{Prefs.DevMode}")
                        .AppendLine("\n###Server Info###")
                        .AppendLine($"Player Count|||{Multiplayer.session.players.Count}")
                        .AppendLine("\n###CPU Info###")
                        .AppendLine($"Processor Name|||{SystemInfo.processorType}")
                        .AppendLine($"Processor Speed (MHz)|||{SystemInfo.processorFrequency}")
                        .AppendLine($"Thread Count|||{SystemInfo.processorCount}")
                        .AppendLine("\n###GPU Info###")
                        .AppendLine($"GPU Family|||{SystemInfo.graphicsDeviceVendor}")
                        .AppendLine($"GPU Type|||{SystemInfo.graphicsDeviceType}")
                        .AppendLine($"GPU Name|||{SystemInfo.graphicsDeviceName}")
                        .AppendLine($"GPU VRAM|||{SystemInfo.graphicsMemorySize}")
                        .AppendLine("\n###RAM Info###")
                        .AppendLine($"Physical Memory Present|||{SystemInfo.systemMemorySize}")
                        .AppendLine("\n###OS Info###")
                        .AppendLine($"OS Type|||{SystemInfo.operatingSystemFamily}")
                        .AppendLine($"OS Name and Version|||{SystemInfo.operatingSystem}");

                    //Save debug info to the zip
                    zip.AddEntry("desync_info", desyncInfo.ToString());
                    desyncReport.AddEntry("desync_info", desyncInfo.ToString());

                    zip.Save();

                    //Add the basic info to the report
                    desyncReport.AddEntry("info", zip["info"].GetBytes());

                    desyncReport.AddEntry("ign", Multiplayer.session.GetPlayerInfo(Multiplayer.session.playerId).username);
                    desyncReport.AddEntry("steamName", SteamUtility.SteamPersonaName);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing desync info: {e}");
            }
        }

        public static void Upload()
        {
            //Report desync to the server
            var request = (HttpWebRequest) WebRequest.Create("http://multiplayer.samboycoding.me/api/desync/upload");
//                    var request = (HttpWebRequest) WebRequest.Create("http://localhost:4193/api/desync/upload");
            request.Method = "POST";
            request.ContentType = "application/zip";

            using (var stream = new MemoryStream())
            {
                desyncReport.Save(stream);

                stream.Seek(0, SeekOrigin.Begin);
                request.ContentLength = stream.Length;
                var data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);

                using (var outStream = request.GetRequestStream())
                    outStream.Write(data, 0, data.Length);
            }

            //TODO: Some user interaction here?
            try
            {
                using (var response = (HttpWebResponse) request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Log.Error("Failed to report desync; Status code " + response.StatusCode);
                    }
                    else
                    {
                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream))
                        {
                            var desyncReportId = reader.ReadToEnd();
                            Log.Message("Desync Reported with ID " + desyncReportId);
                            window.reportId = desyncReportId;
                        }
                    }
                }
            }
            catch (WebException e)
            {
                Log.Error("Failed to report desync; " + e.Message);
                window.reportId = "";
            }

            window.reporting = false;
        }

        private static string GetDesyncStackTraces(ClientSyncOpinion local, ClientSyncOpinion remote, out int index)
        {
            return Multiplayer.game.sync.GetDesyncStackTraces(local, remote, out index);
        }

        private static string FindFileNameForNextDesyncFile()
        {
            //Find all current existing desync zips
            var files = new DirectoryInfo(Multiplayer.DesyncsDir).GetFiles("Desync-*.zip");

            const int MaxFiles = 10;

            //Delete any pushing us over the limit, and reserve room for one more
            if (files.Length > MaxFiles - 1)
                files.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles - 1).Do(f => f.Delete());

            //Find the current max desync number
            int max = 0;
            foreach (var f in files)
                if (int.TryParse(f.Name.Substring(7, f.Name.Length - 7 - 4), out int result) && result > max)
                    max = result;

            return $"Desync-{max + 1:00}";
        }
    }
}