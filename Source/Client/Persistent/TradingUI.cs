using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Multiplayer.Client.Synchronization;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Multiplayer.Client.Persistent
{
    public class TradingWindow : Window
    {
        public static TradingWindow drawingTrade;
        public static bool cancelPressed;

        private static readonly List<TabRecord> tabs = new List<TabRecord>();

        private static readonly HashSet<Tradeable> newTradeables = new HashSet<Tradeable>();
        private static readonly HashSet<Tradeable> oldTradeables = new HashSet<Tradeable>();

        public Dictionary<Tradeable, float> added = new Dictionary<Tradeable, float>();
        private Dialog_Trade dialog;
        public Dictionary<Tradeable, float> removed = new Dictionary<Tradeable, float>();
        private int selectedSession = -1;

        public int selectedTab = -1;

        public TradingWindow()
        {
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);

        public override void DoWindowContents(Rect inRect)
        {
            added.RemoveAll(kv => Time.time - kv.Value > 1f);
            removed.RemoveAll(kv => Time.time - kv.Value > 0.5f && RemoveCachedTradeable(kv.Key));

            tabs.Clear();
            var trading = Multiplayer.WorldComp.trading;
            for (var i = 0; i < trading.Count; i++)
            {
                var j = i;
                tabs.Add(new TabRecord(trading[i].Label, () => selectedTab = j, () => selectedTab == j));
            }

            if (selectedTab == -1 && trading.Count > 0)
                selectedTab = 0;

            if (selectedTab == -1)
            {
                Close();
                return;
            }

            var rows = Mathf.CeilToInt(tabs.Count / 3f);
            inRect.yMin += rows * TabDrawer.TabHeight + 3;
            TabDrawer.DrawTabs(inRect, tabs, rows);

            inRect.yMin += 10f;

            var session = Multiplayer.WorldComp.trading[selectedTab];
            if (session.sessionId != selectedSession)
            {
                RecreateDialog();
                selectedSession = session.sessionId;
            }

            {
                MpTradeSession.SetTradeSession(session);
                drawingTrade = this;

                if (session.deal.ShouldRecache)
                    session.deal.Recache();

                if (session.deal.uiShouldReset != UIShouldReset.None)
                {
                    if (session.deal.uiShouldReset != UIShouldReset.Silent)
                        BeforeCache();

                    dialog.CacheTradeables();
                    dialog.CountToTransferChanged();
                    session.deal.uiShouldReset = UIShouldReset.None;
                }

                if (session.deal.caravanDirty)
                {
                    dialog.CountToTransferChanged();
                    session.deal.caravanDirty = false;
                }

                GUI.BeginGroup(inRect);
                {
                    var groupRect = new Rect(0, 0, inRect.width, inRect.height);
                    dialog.DoWindowContents(groupRect);
                }
                GUI.EndGroup();

                var traderLeavingIn = GetTraderTime(TradeSession.trader);
                if (traderLeavingIn != null)
                {
                    var num = inRect.width - 590f;
                    var position = new Rect(inRect.x + num, inRect.y, inRect.width - num, 58f);
                    var traderNameRect = new Rect(position.x + position.width / 2f, position.y, position.width / 2f - 1f, position.height);
                    var traderTimeRect = traderNameRect.Up(traderNameRect.height - 5f);

                    Text.Anchor = TextAnchor.LowerRight;
                    Widgets.Label(traderTimeRect, "MpTraderLeavesIn".Translate(traderLeavingIn?.ToStringTicksToPeriod()));
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                if (cancelPressed)
                {
                    CancelTradeSession(session);
                    cancelPressed = false;
                }

                session.giftMode = TradeSession.giftMode;

                drawingTrade = null;
                MpTradeSession.SetTradeSession(null);
            }
        }

        private int? GetTraderTime(ITrader trader)
        {
            if (trader is Pawn pawn)
            {
                var lord = pawn.GetLord();
                if (lord == null) return null;

                if (lord.LordJob is LordJob_VisitColony || lord.LordJob is LordJob_TradeWithColony)
                {
                    var transition = lord.graph.transitions.FirstOrDefault(t => t.preActions.Any(a => a is TransitionAction_CheckGiveGift));
                    if (transition == null) return null;

                    var trigger = transition.triggers.OfType<Trigger_TicksPassed>().FirstOrDefault();
                    return trigger?.TicksLeft;
                }
            }
            else if (trader is TradeShip ship)
            {
                return ship.ticksUntilDeparture;
            }

            return null;
        }

        public override void PostClose()
        {
            base.PostClose();

            if (selectedTab >= 0 && Multiplayer.WorldComp.trading.ElementAtOrDefault(selectedTab)?.playerNegotiator.Map == Find.CurrentMap)
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
        }

        private void RecreateDialog()
        {
            var session = Multiplayer.WorldComp.trading[selectedTab];

            CancelDialogTradeCtor.cancel = true;
            MpTradeSession.SetTradeSession(session);

            dialog = new Dialog_Trade(null, null);
            dialog.giftsOnly = session.giftsOnly;
            dialog.sorter1 = TransferableSorterDefOf.Category;
            dialog.sorter2 = TransferableSorterDefOf.MarketValue;
            dialog.CacheTradeables();
            session.deal.uiShouldReset = UIShouldReset.None;

            removed.Clear();
            added.Clear();

            MpTradeSession.SetTradeSession(null);
            CancelDialogTradeCtor.cancel = false;
        }

        public void Notify_RemovedSession(int index)
        {
            if (selectedTab < index) return;

            if (selectedTab > index)
                selectedTab--;

            if (selectedTab == Multiplayer.WorldComp.trading.Count)
                selectedTab--;

            if (selectedTab < 0)
                Close();
        }

        [SyncMethod]
        private static void CancelTradeSession(MpTradeSession session)
        {
            Multiplayer.WorldComp.RemoveTradeSession(session);
        }

        private bool RemoveCachedTradeable(Tradeable t)
        {
            dialog?.cachedTradeables.Remove(t);
            return true;
        }

        private void BeforeCache()
        {
            newTradeables.AddRange(TradeSession.deal.AllTradeables);
            oldTradeables.AddRange(dialog.cachedTradeables);

            foreach (var t in newTradeables)
                if (!t.IsCurrency && !oldTradeables.Contains(t))
                    added[t] = Time.time;

            foreach (var t in oldTradeables)
                if (!t.IsCurrency && !newTradeables.Contains(t))
                    removed[t] = Time.time;

            oldTradeables.Clear();
            newTradeables.Clear();
        }

        public static IEnumerable<Tradeable> AllTradeables()
        {
            foreach (var t in TradeSession.deal.AllTradeables)
                if (!TradeSession.giftMode || t.FirstThingColony != null)
                    yield return t;

            if (drawingTrade != null)
                foreach (var kv in drawingTrade.removed)
                    if (!TradeSession.giftMode || kv.Key.FirstThingColony != null)
                        yield return kv.Key;
        }
    }

    [MpPatch(typeof(JobDriver_TradeWithPawn), "<MakeNewToils>c__Iterator0+<MakeNewToils>c__AnonStorey1", "<>m__1")]
    internal static class ShowTradingWindow
    {
        public static int tradeJobStartedByMe = -1;

        private static void Prefix(Toil ___trade)
        {
            if (___trade.actor.CurJob.loadID == tradeJobStartedByMe)
            {
                Find.WindowStack.Add(new TradingWindow());
                tradeJobStartedByMe = -1;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool))]
    internal static class MakeCancelTradeButtonRed
    {
        private static void Prefix(string label, ref bool __state)
        {
            if (TradingWindow.drawingTrade == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        private static void Postfix(bool __state)
        {
            if (__state)
                GUI.color = Color.white;
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.Close))]
    internal static class HandleCancelTrade
    {
        private static void Prefix()
        {
            if (TradingWindow.drawingTrade != null)
                TradingWindow.cancelPressed = true;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class TradeDealExecutePatch
    {
        private static bool Prefix(TradeDeal __instance)
        {
            if (TradingWindow.drawingTrade != null)
            {
                MpTradeSession.current.TryExecute();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.Reset))]
    internal static class TradeDealResetPatch
    {
        private static bool Prefix(TradeDeal __instance)
        {
            if (TradingWindow.drawingTrade != null)
            {
                MpTradeSession.current.Reset();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TradeUI), nameof(TradeUI.DrawTradeableRow))]
    internal static class TradeableDrawPatch
    {
        private static void Prefix(Tradeable trad, Rect rect)
        {
            if (TradingWindow.drawingTrade != null)
            {
                if (TradingWindow.drawingTrade.added.TryGetValue(trad, out var added))
                {
                    var alpha = 1f - (Time.time - added);
                    Widgets.DrawRectFast(rect, new Color(0, 0.4f, 0, 0.4f * alpha));
                }
                else if (TradingWindow.drawingTrade.removed.TryGetValue(trad, out var removed))
                {
                    var alpha = 1f;
                    Widgets.DrawRectFast(rect, new Color(0.4f, 0, 0, 0.4f * alpha));
                }
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.DoWindowContents))]
    internal static class HandleToggleGiftMode
    {
        private static readonly FieldInfo TradeModeIcon = AccessTools.Field(typeof(Dialog_Trade), nameof(Dialog_Trade.TradeModeIcon));
        private static readonly FieldInfo GiftModeIcon = AccessTools.Field(typeof(Dialog_Trade), nameof(Dialog_Trade.GiftModeIcon));

        private static readonly MethodInfo ButtonImageWithBG = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonImageWithBG));
        private static readonly MethodInfo ToggleGiftModeMethod = AccessTools.Method(typeof(HandleToggleGiftMode), nameof(ToggleGiftMode));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            var insts = new List<CodeInstruction>(e);
            var finder = new CodeFinder(original, insts);

            int tradeMode = finder.Start().Forward(OpCodes.Ldsfld, TradeModeIcon).Forward(OpCodes.Call, ButtonImageWithBG);

            insts.Insert(
                tradeMode + 2,
                new CodeInstruction(OpCodes.Call, ToggleGiftModeMethod),
                new CodeInstruction(OpCodes.Brtrue, insts[tradeMode + 1].operand)
            );

            int giftMode = finder.Start().Forward(OpCodes.Ldsfld, GiftModeIcon).Forward(OpCodes.Call, ButtonImageWithBG);

            insts.Insert(
                giftMode + 2,
                new CodeInstruction(OpCodes.Call, ToggleGiftModeMethod),
                new CodeInstruction(OpCodes.Brtrue, insts[giftMode + 1].operand)
            );

            return insts;
        }

        // Returns whether to jump
        private static bool ToggleGiftMode()
        {
            if (TradingWindow.drawingTrade == null) return false;
            MpTradeSession.current.ToggleGiftMode();
            return true;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.CountHeldBy))]
    internal static class DontShowTraderItemsInGiftMode
    {
        private static void Postfix(Transactor trans, ref int __result)
        {
            if (TradingWindow.drawingTrade != null && TradeSession.giftMode && trans == Transactor.Trader)
                __result = 0;
        }
    }

    [MpPatch(typeof(Dialog_Trade), "<DoWindowContents>m__8")]
    [MpPatch(typeof(Dialog_Trade), "<DoWindowContents>m__9")]
    internal static class FixTradeSorters
    {
        private static void Prefix(ref bool __state)
        {
            var trading = Find.WindowStack.WindowOfType<TradingWindow>();
            if (trading != null)
            {
                MpTradeSession.SetTradeSession(Multiplayer.WorldComp.trading[trading.selectedTab]);
                __state = true;
            }
        }

        private static void Postfix(bool __state)
        {
            if (__state)
                MpTradeSession.SetTradeSession(null);
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.CacheTradeables))]
    internal static class CacheTradeablesPatch
    {
        private static void Postfix(Dialog_Trade __instance)
        {
            if (TradeSession.giftMode)
                __instance.cachedCurrencyTradeable = null;
        }

        // Replace TradeDeal.get_AllTradeables with TradingWindow.AllTradeables
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            var insts = new List<CodeInstruction>(e);
            var finder = new CodeFinder(original, insts);

            for (var i = 0; i < 2; i++)
            {
                int getAllTradeables = finder.Forward(OpCodes.Callvirt, AccessTools.Method(typeof(TradeDeal), "get_AllTradeables"));

                insts.RemoveRange(getAllTradeables - 1, 2);
                insts.Insert(getAllTradeables - 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradingWindow), nameof(TradingWindow.AllTradeables))));
            }

            return insts;
        }
    }
}