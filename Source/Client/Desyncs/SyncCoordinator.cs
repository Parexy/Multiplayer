﻿extern alias zip;
using Harmony;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public class SyncCoordinator
    {
        public bool ShouldCollect => !Multiplayer.IsReplay;

        private ClientSyncOpinion CurrentOpinion
        {
            get
            {
                if (currentOpinion != null)
                    return currentOpinion;

                currentOpinion = new ClientSyncOpinion(TickPatch.Timer)
                {
                    isLocalClientsOpinion = true
                };

                return currentOpinion;
            }
        }

        public readonly List<ClientSyncOpinion> knownClientOpinions = new List<ClientSyncOpinion>();

        public ClientSyncOpinion currentOpinion;

        private int lastValidTick = -1;
        private bool arbiterWasPlayingOnLastValidTick;

        /// <summary>
        /// Adds a client opinion to the <see cref="knownClientOpinions"/> list and checks that it matches the most recent currently in there. If not, a desync event is fired.
        /// </summary>
        /// <param name="newOpinion">The <see cref="ClientSyncOpinion"/> to add and check.</param>
        public void AddClientOpinionAndCheckDesync(ClientSyncOpinion newOpinion)
        {
            //If we've already desynced, don't even bother
            if (Multiplayer.session.desynced) return;

            //If we're skipping ticks, again, don't bother
            if (TickPatch.Skipping) return;

            //If this is the first client opinion we have nothing to compare it with, so just add it
            if (knownClientOpinions.Count == 0)
            {
                knownClientOpinions.Add(newOpinion);
                return;
            }

            if (knownClientOpinions[0].isLocalClientsOpinion == newOpinion.isLocalClientsOpinion)
            {
                knownClientOpinions.Add(newOpinion);
                if (knownClientOpinions.Count > 30)
                    knownClientOpinions.RemoveAt(0);
            }
            else
            {
                //Remove all opinions that started before this one, as it's the most up to date one
                while (knownClientOpinions.Count > 0 && knownClientOpinions[0].startTick < newOpinion.startTick)
                    knownClientOpinions.RemoveAt(0);

                //If there are none left, we don't need to compare this new one
                if (knownClientOpinions.Count == 0)
                {
                    knownClientOpinions.Add(newOpinion);
                }
                else if (knownClientOpinions.First().startTick == newOpinion.startTick)
                {
                    //If these two contain the same tick range - i.e. they start at the same time, cause they should continue to the current tick, then do a comparison.

                    var oldOpinion = knownClientOpinions.RemoveFirst();

                    //Actually do the comparison to find any desync
                    var desyncMessage = oldOpinion.CheckForDesync(newOpinion);

                    if (desyncMessage != null)
                    {
                        MpLog.Log($"Desynced after tick {lastValidTick}: {desyncMessage}");
                        Multiplayer.session.desynced = true;
                        OnMainThread.Enqueue(() => HandleDesync(oldOpinion, newOpinion, desyncMessage));
                    }
                    else
                    {
                        //Update fields 
                        lastValidTick = oldOpinion.startTick;
                        arbiterWasPlayingOnLastValidTick = Multiplayer.session.ArbiterPlaying;
                    }
                }
            }
        }

        /// <summary>
        /// Called by <see cref="AddClientOpinionAndCheckDesync"/> if the newly added opinion doesn't match with what other ones.
        /// </summary>
        /// <param name="oldOpinion">The first up-to-date client opinion present in <see cref="knownClientOpinions"/>, that disagreed with the new one</param>
        /// <param name="newOpinion">The opinion passed to <see cref="AddClientOpinionAndCheckDesync"/> that disagreed with the currently known opinions.</param>
        /// <param name="desyncMessage">The error message that explains exactly what desynced.</param>
        private void HandleDesync(ClientSyncOpinion oldOpinion, ClientSyncOpinion newOpinion, string desyncMessage)
        {
            Multiplayer.Client.Send(Packets.Client_Desynced);

            //Identify which of the two sync infos is local, and which is the remote.
            var local = oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;
            var remote = !oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;

            //Print arbiter desync stacktrace if it exists
            if (local.desyncStackTraces.Any())
                SaveStackTracesToDisk(local, remote);

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

                using (var zip = replay.ZipFile)
                using (var desyncReport = new ZipFile()) //Create a desync report for uploading to the reports server.
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
                    zip.AddEntry("remote_stacks", GetDesyncStackTraces(remote, local, out _));
                    desyncReport.AddEntry("remote_stacks", GetDesyncStackTraces(remote, local, out _));

                    //Prepare the desync info
                    var desyncInfo = new StringBuilder();

                    desyncInfo.AppendLine("###Tick Data###")
                        .AppendLine($"Arbiter Connected And Playing|||{Multiplayer.session.ArbiterPlaying}")
                        .AppendLine($"Last Valid Tick - Local|||{lastValidTick}")
                        .AppendLine($"Arbiter Present on Last Tick|||{arbiterWasPlayingOnLastValidTick}")
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
                        
                        using(var outStream = request.GetRequestStream())
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
                                }
                            }
                        }
                    }
                    catch (WebException e)
                    {
                        Log.Error("Failed to report desync; " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing desync info: {e}");
            }

            Find.WindowStack.windows.Clear();
            Find.WindowStack.Add(new DesyncedWindow(desyncMessage));
        }

        /// <summary>
        /// Saves the local stack traces (saved by calls to <see cref="TryAddStackTraceForDesyncLog"/>) around the area
        /// where a desync occurred to disk, and sends a packet to the arbiter (via the host) to make it do the same
        /// </summary>
        /// <param name="local">The local client's opinion, to dump the stacks from</param>
        /// <param name="remote">A remote client's opinion, used to find where the desync occurred</param>
        private void SaveStackTracesToDisk(ClientSyncOpinion local, ClientSyncOpinion remote)
        {
            Log.Message($"Saving {local.desyncStackTraces.Count} traces to disk");
            
            //Dump the stack traces to disk
            File.WriteAllText("local_traces.txt", GetDesyncStackTraces(local, remote, out var diffAt));

            //Trigger a call to ClientConnection#HandleDebug on the arbiter instance so that arbiter_traces.txt is saved too
            Multiplayer.Client.Send(Packets.Client_Debug, local.startTick, diffAt - 40, diffAt + 40);
        }

        /// <summary>
        /// Get a nicely formatted string containing local stack traces (saved by calls to <see cref="TryAddStackTraceForDesyncLog"/>)
        /// around the area where a desync occurred
        /// </summary>
        /// <param name="dumpFrom">The client's opinion to dump the stacks from</param>
        /// <param name="compareTo">Another client's opinion, used to find where the desync occurred</param>
        /// <param name="diffAt">The index at which the desync stack traces mismatch</param>
        /// <returns></returns>
        private string GetDesyncStackTraces(ClientSyncOpinion dumpFrom, ClientSyncOpinion compareTo, out int diffAt)
        {
            //Find the length of whichever stack trace is shorter.
            diffAt = -1;
            int count = Math.Min(dumpFrom.desyncStackTraceHashes.Count, compareTo.desyncStackTraceHashes.Count);

            //Find the point at which the hashes differ - this is where the desync occurred.
            for (int i = 0; i < count; i++)
                if (dumpFrom.desyncStackTraceHashes[i] != compareTo.desyncStackTraceHashes[i])
                {
                    diffAt = i;
                    break;
                }

            if (diffAt == -1)
                diffAt = count;

            return dumpFrom.GetFormattedStackTracesForRange(diffAt - 40, diffAt + 40, diffAt);
        }

        private string FindFileNameForNextDesyncFile()
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

        /// <summary>
        /// Adds a random state to the commandRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddCommandRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            CurrentOpinion.TryMarkSimulating();
            CurrentOpinion.commandRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        /// Adds a random state to the worldRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddWorldRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            CurrentOpinion.TryMarkSimulating();
            CurrentOpinion.worldRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        /// Adds a random state to the list of the map random state handler for the map with the given id
        /// </summary>
        /// <param name="map">The map id to add the state to</param>
        /// <param name="state">The state to add</param>
        public void TryAddMapRandomState(int map, ulong state)
        {
            if (!ShouldCollect) return;
            CurrentOpinion.TryMarkSimulating();
            CurrentOpinion.GetRandomStatesForMap(map).Add((uint) (state >> 32));
        }

        /// <summary>
        /// Logs the current stack so that in the event of a desync we have some stack traces.
        /// </summary>
        /// <param name="info">Any additional message to be logged with the stack</param>
        /// <param name="doTrace">Set to false to not actually log a stack, only the message</param>
        public void TryAddStackTraceForDesyncLog(string info = null, bool doTrace = true)
        {
            if (!ShouldCollect) return;

            CurrentOpinion.TryMarkSimulating();

            //Get the current stack trace
            var trace = doTrace ? MpUtil.FastStackTrace(4) : new MethodBase[0];

            //Add it to the list
            CurrentOpinion.desyncStackTraces.Add(new StackTraceLogItem {stackTrace = trace, additionalInfo = info});

            //Calculate its hash and add it, for comparison with other opinions.
            currentOpinion.desyncStackTraceHashes.Add(trace.Hash() ^ (info?.GetHashCode() ?? 0));
        }
    }
}