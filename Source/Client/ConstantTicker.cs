#region

extern alias zip;
using System.Linq;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using Verse;

#endregion

namespace Multiplayer.Client
{
    public static class ConstantTicker
    {
        public static bool ticking;

        private static readonly Pawn dummyPawn = new Pawn()
        {
            relations = new Pawn_RelationsTracker(dummyPawn),
        };

        public static void Tick()
        {
            ticking = true;

            try
            {
                //TickResearch();

                // Not really deterministic but here for possible future server-side game state verification
                //Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);
                //TickSync();
                //SyncResearch.ConstantTick();
                //Extensions.PopFaction();

                TickShipCountdown();

                var sync = Multiplayer.game.sync;
                if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.current != null)
                {
                    if (!TickPatch.Skipping && (Multiplayer.LocalServer != null || MultiplayerMod.arbiterInstance))
                        Multiplayer.Client.SendFragmented(Packets.Client_SyncInfo, sync.current.Serialize());

                    sync.Add(sync.current);
                    sync.current = null;
                }
            }
            finally
            {
                ticking = false;
            }
        }

        // Moved from ShipCountdown because the original one is called from Update
        private static void TickShipCountdown()
        {
            if (ShipCountdown.timeLeft > 0f)
            {
                ShipCountdown.timeLeft -= 1 / 60f;

                if (ShipCountdown.timeLeft <= 0f)
                    ShipCountdown.CountdownEnded();
            }
        }

        private static void TickSync()
        {
            foreach (var f in Sync.bufferedFields)
            {
                if (!f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (OnMainThread.CheckShouldRemove(f, k, data))
                        return true;

                    if (!data.sent && TickPatch.Timer - data.timestamp > 30)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                        data.timestamp = TickPatch.Timer;
                    }

                    return false;
                });
            }
        }

        public static void TickResearch()
        {
            var comp = Multiplayer.WorldComp;
            foreach (var factionData in comp.factionData.Values)
            {
                if (factionData.researchManager.currentProj == null)
                    continue;

                Extensions.PushFaction(null, factionData.factionId);

                foreach (var kv in factionData.researchSpeed.data)
                {
                    var pawn = PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == kv.Key);
                    if (pawn == null)
                    {
                        dummyPawn.factionInt = Faction.OfPlayer;
                        pawn = dummyPawn;
                    }

                    Find.ResearchManager.ResearchPerformed(kv.Value, pawn);

                    dummyPawn.factionInt = null;
                }

                Extensions.PopFaction();
            }
        }
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.CancelCountdown))]
    internal static class CancelCancelCountdown
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing;
        }
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.ShipCountdownUpdate))]
    internal static class ShipCountdownUpdatePatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }
}