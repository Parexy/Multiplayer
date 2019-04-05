#region

using System;
using System.Linq;
using System.Text;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    [HotSwappable]
    public static class MainButtonsPatch
    {
        private const float TimelineMargin = 50f;
        private const float TimelineHeight = 35f;

        public const int SkippingWindowId = 26461263;
        public const int TimelineWindowId = 5723681;

        private static bool Prefix()
        {
            Text.Font = GameFont.Small;

            DoDebugInfo();

            if (Multiplayer.IsReplay || TickPatch.Skipping)
            {
                DrawTimeline();
                DrawSkippingWindow();
            }

            DoButtons();

            if (Multiplayer.Client != null && !Multiplayer.IsReplay && Multiplayer.ToggleChatDef.KeyDownEvent)
            {
                Event.current.Use();

                if (ChatWindow.Opened != null)
                    ChatWindow.Opened.Close();
                else
                    OpenChat();
            }

            return Find.Maps.Count > 0;
        }

        private static void DoDebugInfo()
        {
            if (Multiplayer.ShowDevInfo && Multiplayer.Client != null)
            {
                var timerLag = TickPatch.tickUntil - TickPatch.Timer;
                var text =
                    $"{Find.TickManager.TicksGame} {TickPatch.Timer} {TickPatch.tickUntil} {timerLag} {Time.deltaTime * 60f}";
                var rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
                Widgets.Label(rect, text);
            }

            if (Multiplayer.ShowDevInfo && Multiplayer.Client != null && Find.CurrentMap != null)
            {
                var async = Find.CurrentMap.AsyncTime();
                var text = new StringBuilder();
                text.Append(async.mapTicks);

                text.Append($" d: {Find.CurrentMap.designationManager.allDesignations.Count}");

                if (Find.CurrentMap.ParentFaction != null)
                {
                    var faction = Find.CurrentMap.ParentFaction.loadID;
                    var comp = Find.CurrentMap.MpComp();
                    var data = comp.factionMapData.GetValueSafe(faction);

                    if (data != null)
                    {
                        text.Append($" h: {data.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");
                        text.Append($" sg: {data.haulDestinationManager.AllGroupsListForReading.Count}");
                    }
                }

                text.Append($" {Multiplayer.GlobalIdBlock.blockStart + Multiplayer.GlobalIdBlock.current}");

                text.Append($"\n{Sync.bufferedChanges.Sum(kv => kv.Value.Count)}");
                text.Append($"\n{(uint) async.randState} {(uint) (async.randState >> 32)}");
                text.Append(
                    $"\n{(uint) Multiplayer.WorldComp.randState} {(uint) (Multiplayer.WorldComp.randState >> 32)}");
                text.Append(
                    $"\n{async.cmds.Count} {Multiplayer.WorldComp.cmds.Count} {async.slower.forceNormalSpeedUntil} {Multiplayer.WorldComp.asyncTime}");

                var rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text.ToString(), 330f));
                Widgets.Label(rect1, text.ToString());
            }
        }

        private static void OpenChat()
        {
            var chatWindow = new ChatWindow();
            Find.WindowStack.Add(chatWindow);

            if (Multiplayer.session.chatPos != default(Rect))
                chatWindow.windowRect = Multiplayer.session.chatPos;
        }

        private static void DoButtons()
        {
            var y = 10f;
            const float btnHeight = 27f;
            const float btnWidth = 80f;

            var x = UI.screenWidth - btnWidth - 10f;

            var session = Multiplayer.session;

            if (session != null && !Multiplayer.IsReplay)
            {
                var btnRect = new Rect(x, y, btnWidth, btnHeight);

                var chatColor = session.players.Any(p => p.status == PlayerStatus.Desynced) ? "#ff5555" : "#dddddd";
                var hasUnread = session.hasUnread ? "*" : "";
                var chatLabel =
                    $"{"MpChatButton".Translate()} <color={chatColor}>({session.players.Count})</color>{hasUnread}";

                if (Widgets.ButtonText(btnRect, chatLabel))
                    OpenChat();

                if (!TickPatch.Skipping)
                {
                    IndicatorInfo(out var color, out var text, out var slow);

                    var indRect = new Rect(btnRect.x - 25f - 5f + 6f / 2f, btnRect.y + 6f / 2f, 19f, 19f);
                    var biggerRect = new Rect(btnRect.x - 25f - 5f + 2f / 2f, btnRect.y + 2f / 2f, 23f, 23f);

                    if (slow && Widgets.ButtonInvisible(biggerRect))
                        TickPatch.SkipTo(toTickUntil: true, canESC: true);

                    Widgets.DrawRectFast(biggerRect, new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f));
                    Widgets.DrawRectFast(indRect, color);
                    TooltipHandler.TipRegion(indRect, new TipSignal(text, 31641624));
                }

                y += btnHeight;
            }

            if (Multiplayer.ShowDevInfo && Multiplayer.PacketLog != null)
            {
                if (Widgets.ButtonText(new Rect(x, y, btnWidth, btnHeight),
                    $"Sync ({Multiplayer.PacketLog.nodes.Count})"))
                    Find.WindowStack.Add(Multiplayer.PacketLog);

                y += btnHeight;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any())
            {
                if (Widgets.ButtonText(new Rect(x, y, btnWidth, btnHeight), "MpTradingButton".Translate()))
                    Find.WindowStack.Add(new TradingWindow());
                y += btnHeight;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.debugMode)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, y, btnWidth, 30f), $"Debug mode");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        private static void IndicatorInfo(out Color color, out string text, out bool slow)
        {
            var behind = TickPatch.tickUntil - TickPatch.Timer;
            text = "MpTicksBehind".Translate(behind);
            slow = false;

            if (behind > 30)
            {
                color = new Color(0.9f, 0, 0);
                text += "\n\n" + "MpLowerGameSpeed".Translate() + "\n" + "MpForceCatchUp".Translate();
                slow = true;
            }
            else if (behind > 15)
            {
                color = Color.yellow;
            }
            else
            {
                color = new Color(0.0f, 0.8f, 0.0f);
            }
        }

        private static void DrawTimeline()
        {
            var rect = new Rect(TimelineMargin, UI.screenHeight - 35f - TimelineHeight - 10f - 30f,
                UI.screenWidth - TimelineMargin * 2, TimelineHeight + 30f);
            Find.WindowStack.ImmediateWindow(TimelineWindowId, rect, WindowLayer.SubSuper, DrawTimelineWindow, false,
                shadowAlpha: 0);
        }

        private static void DrawTimelineWindow()
        {
            if (Multiplayer.Client == null) return;

            var rect = new Rect(0, 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight);

            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

            var timerStart = Multiplayer.session.replayTimerStart >= 0
                ? Multiplayer.session.replayTimerStart
                : OnMainThread.cachedAtTime;
            var timerEnd = Multiplayer.session.replayTimerEnd >= 0
                ? Multiplayer.session.replayTimerEnd
                : TickPatch.tickUntil;
            var timeLen = timerEnd - timerStart;

            Widgets.DrawLine(new Vector2(rect.xMin + 2f, rect.yMin), new Vector2(rect.xMin + 2f, rect.yMax),
                Color.white, 4f);
            Widgets.DrawLine(new Vector2(rect.xMax - 2f, rect.yMin), new Vector2(rect.xMax - 2f, rect.yMax),
                Color.white, 4f);

            var progress = (TickPatch.Timer - timerStart) / (float) timeLen;
            var progressX = rect.xMin + progress * rect.width;
            Widgets.DrawLine(new Vector2(progressX, rect.yMin), new Vector2(progressX, rect.yMax), Color.green, 7f);

            var mouseX = Event.current.mousePosition.x;
            ReplayEvent mouseEvent = null;

            foreach (var ev in Multiplayer.session.events)
            {
                if (ev.time < timerStart || ev.time > timerEnd)
                    continue;

                var pointX = rect.xMin + (ev.time - timerStart) / (float) timeLen * rect.width;

                //GUI.DrawTexture(new Rect(pointX - 12f, rect.yMin - 24f, 24f, 24f), texture);
                Widgets.DrawLine(new Vector2(pointX, rect.yMin), new Vector2(pointX, rect.yMax), ev.color, 5f);

                if (Mouse.IsOver(rect) && Math.Abs(mouseX - pointX) < 10)
                {
                    mouseX = pointX;
                    mouseEvent = ev;
                }
            }

            if (Mouse.IsOver(rect))
            {
                var mouseProgress = (mouseX - rect.xMin) / rect.width;
                var mouseTimer = timerStart + (int) (timeLen * mouseProgress);

                Widgets.DrawLine(new Vector2(mouseX, rect.yMin), new Vector2(mouseX, rect.yMax), Color.blue, 3f);

                if (Event.current.type == EventType.MouseUp)
                {
                    TickPatch.SkipTo(mouseTimer, canESC: true);

                    if (mouseTimer < TickPatch.Timer)
                        ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
                }

                if (Event.current.isMouse)
                    Event.current.Use();

                var tooltip = $"Tick {mouseTimer}";
                if (mouseEvent != null)
                    tooltip = $"{mouseEvent.name}\n{tooltip}";

                TooltipHandler.TipRegion(rect, new TipSignal(tooltip, 215462143));
                // No delay between the mouseover and showing
                if (TooltipHandler.activeTips.TryGetValue(215462143, out var tip))
                    tip.firstTriggerTime = 0;
            }

            if (TickPatch.Skipping)
            {
                var pct = (TickPatch.skipTo - timerStart) / (float) timeLen;
                var skipToX = rect.xMin + rect.width * pct;
                Widgets.DrawLine(new Vector2(skipToX, rect.yMin), new Vector2(skipToX, rect.yMax), Color.yellow, 4f);
            }
        }

        private static void DrawSkippingWindow()
        {
            if (Multiplayer.Client == null || !TickPatch.Skipping) return;

            var text = $"{TickPatch.skippingTextKey.Translate()}{MpUtil.FixedEllipsis()}";
            var textWidth = Text.CalcSize(text).x;
            var windowWidth = Math.Max(240f, textWidth + 40f);
            var windowHeight = TickPatch.cancelSkip != null ? 100f : 75f;
            var rect = new Rect(0, 0, windowWidth, windowHeight).CenterOn(new Rect(0, 0, UI.screenWidth,
                UI.screenHeight));

            if (TickPatch.canESCSkip && Event.current.type == EventType.KeyUp &&
                Event.current.keyCode == KeyCode.Escape)
            {
                TickPatch.ClearSkipping();
                Event.current.Use();
            }

            Find.WindowStack.ImmediateWindow(SkippingWindowId, rect, WindowLayer.Super, () =>
            {
                var textRect = rect.AtZero();
                if (TickPatch.cancelSkip != null)
                {
                    textRect.yMin += 5f;
                    textRect.height -= 50f;
                }

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Widgets.Label(textRect, text);
                Text.Anchor = TextAnchor.UpperLeft;

                if (TickPatch.cancelSkip != null && Widgets.ButtonText(
                        new Rect(0, textRect.yMax, 100f, 35f).CenteredOnXIn(textRect),
                        TickPatch.skipCancelButtonKey.Translate()))
                    TickPatch.cancelSkip();
            }, absorbInputAroundWindow: true);
        }
    }

    [MpPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    [MpPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
    [MpPatch(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI))]
    internal static class MakeSpaceForReplayTimeline
    {
        private static void Prefix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight -= 60;
        }

        private static void Postfix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight += 60;
        }
    }
}