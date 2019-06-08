extern alias zip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Multiplayer.Client.Windows;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Verse;

namespace Multiplayer.Client.Desyncs
{
    public class SyncCoordinator
    {
        private const int DESYNC_STACK_TRACE_CACHE_SIZE = 3000;

        public readonly List<ClientSyncOpinion> knownClientOpinions = new List<ClientSyncOpinion>();
        internal bool arbiterWasPlayingOnLastValidTick;

        public ClientSyncOpinion currentOpinion;

        internal int lastValidTick = -1;

        public List<StackTraceLogItem> recentTraces = new List<StackTraceLogItem>(DESYNC_STACK_TRACE_CACHE_SIZE + 5);

        public bool ShouldCollect => !Multiplayer.IsReplay;

        private ClientSyncOpinion CurrentOpinion
        {
            get
            {
                if (currentOpinion != null)
                    return currentOpinion;

                currentOpinion = new ClientSyncOpinion(TickPatch.Timer)
                {
                    isLocalClientsOpinion = true,
                    desyncStackTraces = recentTraces.ListFullCopy(),
                    username = "Local Client"
                };

                return currentOpinion;
            }
        }

        /// <summary>
        ///     Adds a client opinion to the <see cref="knownClientOpinions" /> list and checks that it matches the most recent
        ///     currently in there. If not, a desync event is fired.
        /// </summary>
        /// <param name="newOpinion">The <see cref="ClientSyncOpinion" /> to add and check.</param>
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
                lastValidTick = newOpinion.startTick;

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
        ///     Called by <see cref="AddClientOpinionAndCheckDesync" /> if the newly added opinion doesn't match with what other
        ///     ones.
        /// </summary>
        /// <param name="oldOpinion">
        ///     The first up-to-date client opinion present in <see cref="knownClientOpinions" />, that
        ///     disagreed with the new one
        /// </param>
        /// <param name="newOpinion">
        ///     The opinion passed to <see cref="AddClientOpinionAndCheckDesync" /> that disagreed with the
        ///     currently known opinions.
        /// </param>
        /// <param name="desyncMessage">The error message that explains exactly what desynced.</param>
        private void HandleDesync(ClientSyncOpinion oldOpinion, ClientSyncOpinion newOpinion, string desyncMessage)
        {
            Multiplayer.Client.Send(Packet.Client_Desynced);

            Log.Message(
                $"Desynced; old opinion from {oldOpinion.username} doesn't agree with new one from {newOpinion.username}");

            var disagreeingPlayer = Multiplayer.session.players.Find(player => player.username == newOpinion.username);

            var local = oldOpinion.isLocalClientsOpinion ? oldOpinion : newOpinion;
            var remote = oldOpinion.isLocalClientsOpinion ? newOpinion : oldOpinion;

            DesyncReporter.oldOpinion = local;
            DesyncReporter.newOpinion = remote;

            DesyncReporter.window = new DesyncedWindow(desyncMessage);

            GetDesyncStackTraces(local, remote, out var diffTick, out var offsetInTick);

            Find.WindowStack.windows.Clear();
            Find.WindowStack.Add(DesyncReporter.window);

            Multiplayer.Client.Send(Packet.Client_RequestRemoteStacks, disagreeingPlayer.id,
                Multiplayer.session.playerId, diffTick, offsetInTick);
        }

        /// <summary>
        ///     Get a nicely formatted string containing local stack traces (saved by calls to
        ///     <see cref="TryAddStackTraceForDesyncLog" />)
        ///     around the area where a desync occurred
        /// </summary>
        /// <param name="dumpFrom">The client's opinion to dump the stacks from</param>
        /// <param name="compareTo">Another client's opinion, used to find where the desync occurred</param>
        /// <param name="diffAt">The index at which the desync stack traces mismatch</param>
        /// <returns></returns>
        public string GetDesyncStackTraces(ClientSyncOpinion dumpFrom, ClientSyncOpinion compareTo, out int diffTick,
            out int offsetInTick)
        {
            //Find the length of whichever stack trace is shorter.
            var diffAt = -1;
            var count = Math.Min(dumpFrom.desyncStackTraceHashes.Count, compareTo.desyncStackTraceHashes.Count);

            //Find the point at which the hashes differ - this is where the desync occurred.
            for (var i = 0; i < count; i++)
                if (dumpFrom.desyncStackTraceHashes[i] != compareTo.desyncStackTraceHashes[i])
                {
                    diffAt = i;
                    break;
                }

            if (diffAt == -1)
                diffAt = count - 1;

            Log.Message(
                $"There are {dumpFrom.desyncStackTraces.Count} traces, {dumpFrom.desyncStackTraceHashes.Count} hashes, and the desync is at position {diffAt}");

            diffAt += dumpFrom.desyncStackTraces.Count - dumpFrom.desyncStackTraceHashes.Count - 1;

            diffTick = dumpFrom.desyncStackTraces[diffAt].lastValidTick;
            var dtCopy = diffTick;

            //The difference between the first stack in this tick and the one we want
            offsetInTick = diffAt - dumpFrom.desyncStackTraces.FindIndex(stack => stack.lastValidTick == dtCopy);

            return dumpFrom.GetFormattedStackTracesForRange(diffAt - 40, diffAt + 40, diffAt);
        }


        /// <summary>
        ///     Adds a random state to the commandRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddCommandRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            CurrentOpinion.TryMarkSimulating();
            CurrentOpinion.commandRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        ///     Adds a random state to the worldRandomStates list
        /// </summary>
        /// <param name="state">The state to add</param>
        public void TryAddWorldRandomState(ulong state)
        {
            if (!ShouldCollect) return;
            CurrentOpinion.TryMarkSimulating();
            CurrentOpinion.worldRandomStates.Add((uint) (state >> 32));
        }

        /// <summary>
        ///     Adds a random state to the list of the map random state handler for the map with the given id
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
        ///     Logs the current stack so that in the event of a desync we have some stack traces.
        /// </summary>
        /// <param name="info">Any additional message to be logged with the stack</param>
        /// <param name="doTrace">Set to false to not actually log a stack, only the message</param>
        public void TryAddStackTraceForDesyncLog(string info = null, bool doTrace = true)
        {
            if (!ShouldCollect) return;

            CurrentOpinion.TryMarkSimulating();

            //Get the current stack trace
            var trace = doTrace ? MpUtil.FastStackTrace(4) : new MethodBase[0];
            var item = new StackTraceLogItem {stackTrace = trace, additionalInfo = info};

            //Add it to the list
            recentTraces.Add(item);

            if (recentTraces.Count > DESYNC_STACK_TRACE_CACHE_SIZE)
                recentTraces.RemoveRange(0, recentTraces.Count - DESYNC_STACK_TRACE_CACHE_SIZE);

            CurrentOpinion.desyncStackTraces = recentTraces;

            //Calculate its hash and add it, for comparison with other opinions.
            currentOpinion.desyncStackTraceHashes.Add(trace.Hash() ^ (info?.GetHashCode() ?? 0));
        }
    }
}