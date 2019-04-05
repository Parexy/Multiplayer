﻿#region

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

#endregion

namespace Multiplayer.Client
{
    public class MpTradeSession : IExposable, ISessionWithTransferables
    {
        public static MpTradeSession current;
        public MpTradeDeal deal;
        public bool giftMode;
        public bool giftsOnly;
        public Pawn playerNegotiator;

        public int sessionId;
        public ITrader trader;

        public MpTradeSession()
        {
        }

        private MpTradeSession(ITrader trader, Pawn playerNegotiator, bool giftMode)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();

            this.trader = trader;
            this.playerNegotiator = playerNegotiator;
            this.giftMode = giftMode;
            giftsOnly = giftMode;
        }

        public string Label
        {
            get
            {
                if (trader is Pawn pawn)
                    return pawn.Faction.Name;
                return trader.TraderName;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");

            var trader = (ILoadReferenceable) this.trader;
            Scribe_References.Look(ref trader, "trader");
            this.trader = (ITrader) trader;

            Scribe_References.Look(ref playerNegotiator, "playerNegotiator");
            Scribe_Values.Look(ref giftMode, "giftMode");
            Scribe_Values.Look(ref giftsOnly, "giftsOnly");

            Scribe_Deep.Look(ref deal, "tradeDeal", this);
        }

        public int SessionId => sessionId;

        public Transferable GetTransferableByThingId(int thingId)
        {
            for (var i = 0; i < deal.tradeables.Count; i++)
            {
                var tr = deal.tradeables[i];
                if (tr.FirstThingColony?.thingIDNumber == thingId)
                    return tr;
                if (tr.FirstThingTrader?.thingIDNumber == thingId)
                    return tr;
            }

            return null;
        }

        public void Notify_CountChanged(Transferable tr)
        {
            deal.caravanDirty = true;
        }

        public static void TryCreate(ITrader trader, Pawn playerNegotiator, bool giftMode)
        {
            // todo show error messages?
            if (Multiplayer.WorldComp.trading.Any(s => s.trader == trader))
                return;

            if (Multiplayer.WorldComp.trading.Any(s => s.playerNegotiator == playerNegotiator))
                return;

            var session = new MpTradeSession(trader, playerNegotiator, giftMode);
            Multiplayer.WorldComp.trading.Add(session);

            CancelTradeDealReset.cancel = true;
            SetTradeSession(session, true);

            try
            {
                session.deal = new MpTradeDeal(session);

                var permSilver = ThingMaker.MakeThing(ThingDefOf.Silver, null);
                permSilver.stackCount = 0;
                session.deal.permanentSilver = permSilver;
                session.deal.AddToTradeables(permSilver, Transactor.Trader);

                session.deal.AddAllTradeables();
                session.StartWaitingJobs();
            }
            finally
            {
                SetTradeSession(null);
                CancelTradeDealReset.cancel = false;
            }
        }

        // todo come back to it when the map doesn't get paused during trading
        private void StartWaitingJobs()
        {
        }

        public bool ShouldCancel()
        {
            if (!trader.CanTradeNow)
                return true;

            if (playerNegotiator.Drafted)
                return true;

            if (trader is Pawn traderPawn)
            {
                if (!traderPawn.Spawned || !playerNegotiator.Spawned)
                    return true;
                return traderPawn.Position.DistanceToSquared(playerNegotiator.Position) > 2 * 2;
            }

            if (trader is SettlementBase traderBase)
            {
                var caravan = playerNegotiator.GetCaravan();
                if (caravan == null)
                    return true;

                if (CaravanVisitUtility.SettlementVisitedNow(caravan) != traderBase)
                    return true;
            }

            return false;
        }

        [SyncMethod]
        public void TryExecute()
        {
            SetTradeSession(this);

            deal.recacheColony = true;
            deal.recacheTrader = true;
            deal.Recache();

            var executed = deal.TryExecute(out var traded);
            SetTradeSession(null);

            if (executed)
                Multiplayer.WorldComp.RemoveTradeSession(this);
        }

        [SyncMethod]
        public void Reset()
        {
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        [SyncMethod]
        public void ToggleGiftMode()
        {
            giftMode = !giftMode;
            deal.tradeables.ForEach(t => t.countToTransfer = 0);
            deal.uiShouldReset = UIShouldReset.Silent;
        }

        public static void SetTradeSession(MpTradeSession session, bool force = false)
        {
            if (!force && TradeSession.deal == session?.deal) return;

            current = session;
            TradeSession.trader = session?.trader;
            TradeSession.playerNegotiator = session?.playerNegotiator;
            TradeSession.giftMode = session?.giftMode ?? false;
            TradeSession.deal = session?.deal;
        }
    }

    public class MpTradeDeal : TradeDeal, IExposable
    {
        private static readonly HashSet<Thing> newThings = new HashSet<Thing>();
        private static readonly HashSet<Thing> oldThings = new HashSet<Thing>();
        public bool caravanDirty;

        public Thing permanentSilver;
        public bool recacheColony;

        public HashSet<Thing> recacheThings = new HashSet<Thing>();
        public bool recacheTrader;
        public MpTradeSession session;

        public UIShouldReset uiShouldReset;

        public MpTradeDeal(MpTradeSession session)
        {
            this.session = session;
        }

        public bool ShouldRecache => recacheColony || recacheTrader || recacheThings.Count > 0;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref permanentSilver, "permanentSilver");
            Scribe_Collections.Look(ref tradeables, "tradeables", LookMode.Deep);
        }

        public void Recache()
        {
            if (recacheColony)
                CheckAddRemoveColony();

            if (recacheTrader)
                CheckAddRemoveTrader();

            if (recacheThings.Count > 0)
                CheckReassign();

            UpdateCurrencyCount();

            uiShouldReset = UIShouldReset.Full;
            recacheThings.Clear();
            recacheColony = false;
            recacheTrader = false;
        }

        private void CheckAddRemoveColony()
        {
            foreach (var t in session.trader.ColonyThingsWillingToBuy(session.playerNegotiator))
                newThings.Add(t);

            for (var i = tradeables.Count - 1; i >= 0; i--)
            {
                var tradeable = tradeables[i];
                var toRemove = 0;

                for (var j = tradeable.thingsColony.Count - 1; j >= 0; j--)
                {
                    var thingColony = tradeable.thingsColony[j];
                    if (!newThings.Contains(thingColony))
                        toRemove++;
                    else
                        oldThings.Add(thingColony);
                }

                if (toRemove == 0) continue;

                if (toRemove == tradeable.thingsColony.Count + tradeable.thingsTrader.Count)
                    tradeables.RemoveAt(i);
                else
                    tradeable.thingsColony.RemoveAll(t => !newThings.Contains(t));
            }

            foreach (var newThing in newThings)
                if (!oldThings.Contains(newThing))
                    AddToTradeables(newThing, Transactor.Colony);

            newThings.Clear();
            oldThings.Clear();
        }

        private void CheckAddRemoveTrader()
        {
            newThings.Add(permanentSilver);

            foreach (var t in session.trader.Goods)
                newThings.Add(t);

            for (var i = tradeables.Count - 1; i >= 0; i--)
            {
                var tradeable = tradeables[i];
                var toRemove = 0;

                for (var j = tradeable.thingsTrader.Count - 1; j >= 0; j--)
                {
                    var thingTrader = tradeable.thingsTrader[j];
                    if (!newThings.Contains(thingTrader))
                        toRemove++;
                    else
                        oldThings.Add(thingTrader);
                }

                if (toRemove == 0) continue;

                if (toRemove == tradeable.thingsColony.Count + tradeable.thingsTrader.Count)
                    tradeables.RemoveAt(i);
                else
                    tradeable.thingsTrader.RemoveAll(t => !newThings.Contains(t));
            }

            foreach (var newThing in newThings)
                if (!oldThings.Contains(newThing))
                    AddToTradeables(newThing, Transactor.Trader);

            newThings.Clear();
            oldThings.Clear();
        }

        private void CheckReassign()
        {
            for (var i = tradeables.Count - 1; i >= 0; i--)
            {
                var tradeable = tradeables[i];

                CheckReassign(tradeable, Transactor.Colony);
                CheckReassign(tradeable, Transactor.Trader);

                if (recacheThings.Count == 0) break;
            }
        }

        private void CheckReassign(Tradeable tradeable, Transactor side)
        {
            var things = side == Transactor.Colony ? tradeable.thingsColony : tradeable.thingsTrader;

            for (var j = things.Count - 1; j >= 1; j--)
            {
                var thing = things[j];
                var mode = tradeable.TraderWillTrade ? TransferAsOneMode.Normal : TransferAsOneMode.InactiveTradeable;

                if (recacheThings.Contains(thing))
                {
                    if (!TransferableUtility.TransferAsOne(tradeable.AnyThing, thing, mode))
                        things.RemoveAt(j);
                    else
                        AddToTradeables(thing, side);
                }
            }
        }
    }

    public enum UIShouldReset
    {
        None,
        Silent,
        Full
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.Reset))]
    internal static class CancelTradeDealReset
    {
        public static bool cancel;

        private static bool Prefix()
        {
            return !cancel && Scribe.mode != LoadSaveMode.LoadingVars;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class CancelDialogTrade
    {
        private static bool Prefix(Window window)
        {
            if (window is Dialog_Trade && (Multiplayer.ExecutingCmds || Multiplayer.Ticking))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(Pawn), typeof(ITrader), typeof(bool)})]
    internal static class CancelDialogTradeCtor
    {
        public static bool cancel;

        private static bool Prefix(Pawn playerNegotiator, ITrader trader, bool giftsOnly)
        {
            if (cancel) return false;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                MpTradeSession.TryCreate(trader, playerNegotiator, giftsOnly);

                if (TickPatch.currentExecutingCmdIssuedBySelf)
                    Find.WindowStack.Add(new TradingWindow());

                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival),
        nameof(IncidentWorker_TraderCaravanArrival.TryExecuteWorker))]
    internal static class ArriveAtCenter
    {
        private static void Prefix(IncidentParms parms)
        {
            //if (MpVersion.IsDebug && Prefs.DevMode)
            //    parms.spawnCenter = (parms.target as Map).Center;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class NullCheckDialogTrade
    {
        private static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e)
        {
            var insts = new List<CodeInstruction>(e);
            var local = gen.DeclareLocal(typeof(Dialog_Trade));

            for (var i = 0; i < insts.Count; i++)
            {
                var inst = insts[i];
                yield return inst;

                if (inst.opcode == OpCodes.Callvirt &&
                    ((MethodInfo) inst.operand).Name == nameof(WindowStack.WindowOfType))
                {
                    var label = gen.DefineLabel();
                    insts[i + 2].labels.Add(label);

                    yield return new CodeInstruction(OpCodes.Stloc, local);
                    yield return new CodeInstruction(OpCodes.Ldloc, local);
                    yield return new CodeInstruction(OpCodes.Brfalse, label);
                    yield return new CodeInstruction(OpCodes.Ldloc, local);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Reachability), nameof(Reachability.ClearCache))]
    internal static class ReachabilityChanged
    {
        private static void Postfix(Reachability __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(Area_Home), nameof(Area_Home.Set))]
    internal static class AreaHomeChanged
    {
        private static void Postfix(Area_Home __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.Map);
        }
    }

    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.AddHaulDestination))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.RemoveHaulDestination))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.SetCellFor))]
    [MpPatch(typeof(HaulDestinationManager), nameof(HaulDestinationManager.ClearCellFor))]
    internal static class HaulDestinationChanged
    {
        private static void Postfix(HaulDestinationManager __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.StageChanged))]
    internal static class RottableStageChanged
    {
        private static void Postfix(CompRottable __instance)
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.parent.Map);
        }
    }

    [MpPatch(typeof(ListerThings), nameof(ListerThings.Add))]
    [MpPatch(typeof(ListerThings), nameof(ListerThings.Remove))]
    internal static class ListerThingsChangedItem
    {
        private static void Postfix(ListerThings __instance, Thing t)
        {
            if (Multiplayer.Client == null) return;
            if (t.def.category == ThingCategory.Item && ListerThings.EverListable(t.def, __instance.use))
                Multiplayer.WorldComp.DirtyColonyTradeForMap(t.Map);
        }
    }

    [MpPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned))]
    [MpPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeUndowned))]
    internal static class PawnDownedStateChanged
    {
        private static void Postfix(Pawn_HealthTracker __instance)
        {
            if (Multiplayer.Client != null)
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader))]
    [HarmonyPatch(nameof(CompPowerTrader.PowerOn), MethodType.Setter)]
    internal static class OrbitalTradeBeaconPowerChanged
    {
        private static void Postfix(CompPowerTrader __instance, bool value)
        {
            if (Multiplayer.Client == null) return;
            if (!(__instance.parent is Building_OrbitalTradeBeacon)) return;
            if (value == __instance.powerOnInt) return;
            if (!Multiplayer.WorldComp.trading.Any(t => t.trader is TradeShip)) return;

            // For trade ships
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.parent.Map);
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.HitPoints), MethodType.Setter)]
    internal static class ThingHitPointsChanged
    {
        private static void Prefix(Thing __instance, int value, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            __state = __instance.def.category == ThingCategory.Item && value != __instance.hitPointsInt;
        }

        private static void Postfix(Thing __instance, bool __state)
        {
            if (__state)
                Multiplayer.WorldComp.DirtyTradeForSpawnedThing(__instance);
        }
    }

    [MpPatch(typeof(ThingOwner), nameof(ThingOwner.NotifyAdded))]
    [MpPatch(typeof(ThingOwner), nameof(ThingOwner.NotifyAddedAndMergedWith))]
    [MpPatch(typeof(ThingOwner), nameof(ThingOwner.NotifyRemoved))]
    internal static class ThingOwner_ChangedPatch
    {
        private static void Postfix(ThingOwner __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.owner is Pawn_InventoryTracker inv)
            {
                ITrader trader = null;

                if (inv.pawn.GetLord()?.LordJob is LordJob_TradeWithColony lordJob)
                    // Carrier inventory changed
                    trader = lordJob.lord.ownedPawns.FirstOrDefault(p =>
                        p.GetTraderCaravanRole() == TraderCaravanRole.Trader);
                else if (inv.pawn.trader != null)
                    // Trader inventory changed
                    trader = inv.pawn;

                if (trader != null)
                    Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader);
            }
            else if (__instance.owner is SettlementBase_TraderTracker trader)
            {
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader.settlement);
            }
            else if (__instance.owner is TradeShip ship)
            {
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(ship);
            }
        }
    }

    [MpPatch(typeof(Lord), nameof(Lord.AddPawn))]
    [MpPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
    internal static class Lord_TradeChanged
    {
        private static void Postfix(Lord __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.LordJob is LordJob_TradeWithColony)
            {
                // Chattel changed
                ITrader trader =
                    __instance.ownedPawns.FirstOrDefault(p => p.GetTraderCaravanRole() == TraderCaravanRole.Trader);
                Multiplayer.WorldComp.DirtyTraderTradeForTrader(trader);
            }
            else if (__instance.LordJob is LordJob_PrisonBreak)
            {
                // Prisoners in a break can't be sold
                Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.Map);
            }
        }
    }

    [MpPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    [MpPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.ClearMentalStateDirect))]
    internal static class MentalStateChanged
    {
        private static void Postfix(MentalStateHandler __instance)
        {
            if (Multiplayer.Client == null) return;

            // Pawns in a mental state can't be sold
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Notify_Starting))]
    internal static class JobExitMapStarted
    {
        private static void Postfix(JobDriver __instance)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.job.exitMapOnArrival) Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.pawn.Map);
        }
    }

    [HarmonyPatch(typeof(SettlementBase_TraderTracker), nameof(SettlementBase_TraderTracker.TraderTrackerTick))]
    internal static class DontDestroyStockWhileTrading
    {
        private static bool Prefix(SettlementBase_TraderTracker __instance)
        {
            return Multiplayer.Client == null ||
                   !Multiplayer.WorldComp.trading.Any(t => t.trader == __instance.settlement);
        }
    }

    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.DoListChangedNotifications))]
    internal static class MapPawnsChanged
    {
        private static void Postfix(MapPawns __instance)
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.DirtyColonyTradeForMap(__instance.map);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.RecalculateLifeStageIndex))]
    internal static class PawnLifeStageChanged
    {
        private static void Postfix(Pawn_AgeTracker __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!__instance.pawn.Spawned) return;

            Multiplayer.WorldComp.DirtyTradeForSpawnedThing(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTick))]
    internal static class PawnAgeChanged
    {
        private static void Prefix(Pawn_AgeTracker __instance, ref int __state)
        {
            __state = __instance.AgeBiologicalYears;
        }

        private static void Postfix(Pawn_AgeTracker __instance, int __state)
        {
            if (Multiplayer.Client == null) return;
            if (__state == __instance.AgeBiologicalYears) return;

            // todo?
        }
    }

    [HarmonyPatch(typeof(TransferableUtility), nameof(TransferableUtility.TransferAsOne))]
    internal static class TransferAsOneAgeCheck_Patch
    {
        private static readonly MethodInfo AgeBiologicalFloat =
            AccessTools.Method(typeof(Pawn_AgeTracker), "get_AgeBiologicalYearsFloat");

        private static readonly MethodInfo AgeBiologicalInt =
            AccessTools.Method(typeof(Pawn_AgeTracker), "get_AgeBiologicalYears");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == AgeBiologicalFloat)
                {
                    yield return new CodeInstruction(OpCodes.Callvirt, AgeBiologicalInt);
                    yield return new CodeInstruction(OpCodes.Conv_R4);
                    continue;
                }

                yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.InSellablePosition))]
    internal static class InSellablePositionPatch
    {
        // todo actually handle this
        private static void Postfix(Thing t, ref bool __result, ref string reason)
        {
            if (Multiplayer.Client == null) return;

            //__result = t.Spawned;
            //reason = null;
        }
    }
}