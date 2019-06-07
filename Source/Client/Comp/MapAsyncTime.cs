﻿extern alias zip;
using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using Multiplayer.Client.Synchronization;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using Multiplayer.Server;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Comp
{
    [MpPatch(typeof(Map), nameof(Map.MapPreTick))]
    [MpPatch(typeof(Map), nameof(Map.MapPostTick))]
    internal static class CancelMapManagersTick
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || MapAsyncTimeComp.tickingMap != null;
        }
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    internal static class DisableAutosaver
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    internal static class MapUpdateMarker
    {
        public static bool updating;

        private static void Prefix()
        {
            updating = true;
        }

        private static void Postfix()
        {
            updating = false;
        }
    }

    [MpPatch(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First))]
    [MpPatch(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First))]
    [MpPatch(typeof(RegionGrid), nameof(RegionGrid.UpdateClean))]
    [MpPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms))]
    internal static class CancelMapManagersUpdate
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || !MapUpdateMarker.updating;
        }
    }

    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    internal static class DateNotifierPatch
    {
        private static void Prefix(DateNotifier __instance, ref int? __state)
        {
            if (Multiplayer.Client == null && Multiplayer.RealPlayerFaction != null) return;

            var map = __instance.FindPlayerHomeWithMinTimezone();
            if (map == null) return;

            __state = Find.TickManager.TicksGame;
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);
        }

        private static void Postfix(int? __state)
        {
            if (!__state.HasValue) return;
            Find.TickManager.DebugSetTicksGame(__state.Value);
            FactionContext.Pop();
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        private static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            var comp = t.Map.AsyncTime();
            var tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        private static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            var comp = t.Map.AsyncTime();
            var tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    internal static class TimeControlsMarker
    {
        public static bool drawingTimeControls;

        private static void Prefix()
        {
            drawingTimeControls = true;
        }

        private static void Postfix()
        {
            drawingTimeControls = false;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    [HotSwappable]
    public static class TimeControlPatch
    {
        private static TimeSpeed prevSpeed;
        private static TimeSpeed savedSpeed;
        private static bool keyPressed;

        private static void Prefix(ref ITickable __state)
        {
            if (Multiplayer.Client == null) return;
            if (!WorldRendererUtility.WorldRenderedNow && Find.CurrentMap == null) return;

            ITickable tickable = Multiplayer.WorldComp;
            if (!WorldRendererUtility.WorldRenderedNow && Multiplayer.WorldComp.asyncTime)
                tickable = Find.CurrentMap.AsyncTime();

            var speed = tickable.TimeSpeed;
            if (Multiplayer.IsReplay)
                speed = TickPatch.replayTimeSpeed;

            savedSpeed = Find.TickManager.CurTimeSpeed;

            Find.TickManager.CurTimeSpeed = speed;
            prevSpeed = speed;
            keyPressed = Event.current.isKey;
            __state = tickable;
        }

        private static void Postfix(ITickable __state, Rect timerRect)
        {
            if (__state == null) return;

            var rect = new Rect(timerRect.x, timerRect.y, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
            var normalSpeed = __state.ActualRateMultiplier(TimeSpeed.Normal);
            var fastSpeed = __state.ActualRateMultiplier(TimeSpeed.Fast);

            if (normalSpeed == 0f) // Completely paused
                Widgets.DrawLineHorizontal(rect.x + rect.width, rect.y + rect.height / 2f, rect.width * 3f);
            else if (normalSpeed == fastSpeed) // Slowed down
                Widgets.DrawLineHorizontal(rect.x + rect.width * 2f, rect.y + rect.height / 2f, rect.width * 2f);

            var newSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.CurTimeSpeed = savedSpeed;

            if (prevSpeed == newSpeed) return;

            if (Multiplayer.IsReplay)
                TickPatch.replayTimeSpeed = newSpeed;

            // Prevent multiple players changing the speed too quickly
            if (keyPressed && Time.realtimeSinceStartup - MultiplayerWorldComp.lastSpeedChange < 0.4f)
                return;

            TimeControl.SendTimeChange(__state, newSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class DoSingleTickShortcut
    {
        private static bool Prefix()
        {
            if (Multiplayer.Client == null || !TimeControlsMarker.drawingTimeControls)
                return true;

            if (TickPatch.Timer < TickPatch.tickUntil)
            {
                var replaySpeed = TickPatch.replayTimeSpeed;
                TickPatch.replayTimeSpeed = TimeSpeed.Normal;
                TickPatch.accumulator = 1;

                TickPatch.Tick();

                TickPatch.accumulator = 0;
                TickPatch.replayTimeSpeed = replaySpeed;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ShowGroupFrames), MethodType.Getter)]
    internal static class AlwaysShowColonistBarFrames
    {
        private static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    [HotSwappable]
    public static class ColonistBarTimeControl
    {
        private static void Prefix(ref bool __state)
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
            {
                DrawButtons();
                __state = true;
            }
        }

        private static void Postfix(bool __state)
        {
            if (!__state)
                DrawButtons();
        }

        private static void DrawButtons()
        {
            if (Multiplayer.Client == null) return;

            var bar = Find.ColonistBar;
            if (bar.Entries.Count == 0) return;

            var curGroup = -1;
            foreach (var entry in bar.Entries)
            {
                if (entry.map == null || curGroup == entry.group) continue;

                var alpha = 1.0f;
                if (entry.map != Find.CurrentMap || WorldRendererUtility.WorldRenderedNow)
                    alpha = 0.75f;

                var rect = bar.drawer.GroupFrameRect(entry.group);
                var button = new Rect(rect.x - TimeControls.TimeButSize.x / 2f, rect.yMax - TimeControls.TimeButSize.y / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
                var asyncTime = entry.map.AsyncTime();

                if (Multiplayer.WorldComp.asyncTime)
                {
                    TimeControl.TimeControlButton(button, asyncTime, alpha);
                }
                else if (asyncTime.TickRateMultiplier(TimeSpeed.Normal) == 0f) // Blocking pause
                {
                    Widgets.DrawRectFast(button, new Color(1f, 0.5f, 0.5f, 0.4f * alpha));
                    Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[0]);
                }

                curGroup = entry.group;
            }
        }
    }

    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    internal static class MainButtonWorldTimeControl
    {
        private static void Prefix(MainButtonWorker __instance, Rect rect, ref Rect? __state)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.def != MainButtonDefOf.World) return;
            if (__instance.Disabled) return;
            if (Find.CurrentMap == null) return;
            if (!Multiplayer.WorldComp.asyncTime) return;

            var button = new Rect(rect.xMax - TimeControls.TimeButSize.x - 5f, rect.y + (rect.height - TimeControls.TimeButSize.y) / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
            __state = button;

            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
                TimeControl.TimeControlButton(__state.Value, Multiplayer.WorldComp, 0.5f);
        }

        private static void Postfix(MainButtonWorker __instance, Rect? __state)
        {
            if (__state == null) return;

            if (Event.current.type == EventType.Repaint)
                TimeControl.TimeControlButton(__state.Value, Multiplayer.WorldComp, 0.5f);
        }
    }

    internal static class TimeControl
    {
        public static void TimeControlButton(Rect button, ITickable tickable, float alpha)
        {
            Widgets.DrawRectFast(button, new Color(0.5f, 0.5f, 0.5f, 0.4f * alpha));

            var speed = (int) tickable.TimeSpeed;
            if (Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[speed]))
            {
                var dir = Event.current.button == 0 ? 1 : -1;
                SendTimeChange(tickable, (TimeSpeed) GenMath.PositiveMod(speed + dir, (int) TimeSpeed.Ultrafast));
                Event.current.Use();
            }
        }

        public static void SendTimeChange(ITickable tickable, TimeSpeed newSpeed)
        {
            if (tickable is MultiplayerWorldComp)
                Multiplayer.Client.SendCommand(CommandType.WorldTimeSpeed, ScheduledCommand.Global, (byte) newSpeed);
            else if (tickable is MapAsyncTimeComp comp)
                Multiplayer.Client.SendCommand(CommandType.MapTimeSpeed, comp.map.uniqueID, (byte) newSpeed);
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    internal static class PreDrawCalcMarker
    {
        public static Pawn calculating;

        private static void Prefix(PawnTweener __instance)
        {
            calculating = __instance.pawn;
        }

        private static void Postfix()
        {
            calculating = null;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    internal static class TickRateMultiplierPatch
    {
        private static void Postfix(ref float __result)
        {
            if (PreDrawCalcMarker.calculating == null) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var map = PreDrawCalcMarker.calculating.Map ?? Find.CurrentMap;
            var asyncTime = map.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = TickPatch.Skipping ? 6 : asyncTime.ActualRateMultiplier(timeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    internal static class TickManagerPausedPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var asyncTime = Find.CurrentMap.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = asyncTime.ActualRateMultiplier(timeSpeed) == 0;
        }
    }

    [MpPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    [MpPatch(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick))]
    public class StorytellerTickPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.AllIncidentTargets), MethodType.Getter)]
    public class StorytellerTargetsPatch
    {
        private static void Postfix(List<IIncidentTarget> __result)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.MapContext != null)
            {
                __result.Clear();
                __result.Add(Multiplayer.MapContext);
            }
            else if (MultiplayerWorldComp.tickingWorld)
            {
                __result.Clear();

                foreach (var caravan in Find.WorldObjects.Caravans)
                    if (caravan.IsPlayerControlled)
                        __result.Add(caravan);

                __result.Add(Find.World);
            }
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Notify_GeneratedPotentiallyHostileMap))]
    internal static class GeneratedHostileMapPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }

        private static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            // The newly generated map
            Find.Maps.LastOrDefault()?.AsyncTime().slower.SignalForceNormalSpeedShort();
        }
    }

    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
    internal static class MapContextIncidentParms
    {
        private static void Prefix(IIncidentTarget target, ref Map __state)
        {
            if (MultiplayerWorldComp.tickingWorld && target is Map map)
            {
                MapAsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        private static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                MapAsyncTimeComp.tickingMap = null;
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    internal static class MapContextIncidentExecute
    {
        private static void Prefix(IncidentParms parms, ref Map __state)
        {
            if (MultiplayerWorldComp.tickingWorld && parms.target is Map map)
            {
                MapAsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        private static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                MapAsyncTimeComp.tickingMap = null;
            }
        }
    }

    public class MapAsyncTimeComp : IExposable, ITickable
    {
        public static Map tickingMap;
        public static Map executingCmdMap;

        public static bool keepTheMap;

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();
        public bool forcedNormalSpeed;

        public Map map;
        public int mapTicks;

        private bool nothingHappeningCached;
        private Storyteller prevStoryteller;
        private StoryWatcher prevStoryWatcher;

        private TimeSnapshot? prevTime;

        // Shared random state for ticking and commands
        public ulong randState = 1;
        public TimeSlower slower = new TimeSlower();

        public Storyteller storyteller;
        public StoryWatcher storyWatcher;
        public TickList tickListLong = new TickList(TickerType.Long);

        public TickList tickListNormal = new TickList(TickerType.Normal);
        public TickList tickListRare = new TickList(TickerType.Rare);
        private TimeSpeed timeSpeedInt;

        public MapAsyncTimeComp(Map map)
        {
            this.map = map;
        }

        public bool Paused => this.ActualRateMultiplier(TimeSpeed) == 0f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            Scribe_Deep.Look(ref storyteller, "storyteller");

            Scribe_Deep.Look(ref storyWatcher, "storyWatcher");
            if (Scribe.mode == LoadSaveMode.LoadingVars && storyWatcher == null)
                storyWatcher = new StoryWatcher();

            ScribeUtil.LookULong(ref randState, "randState", 1);
        }

        public float TickRateMultiplier(TimeSpeed speed)
        {
            var comp = map.MpComp();
            if (comp.transporterLoading != null || comp.caravanForming != null || comp.mapDialogs.Any() || Multiplayer.WorldComp.trading.Any(t => t.playerNegotiator.Map == map))
                return 0f;

            if (mapTicks < slower.forceNormalSpeedUntil)
                return speed == TimeSpeed.Paused ? 0 : 1;

            switch (speed)
            {
                case TimeSpeed.Paused:
                    return 0f;
                case TimeSpeed.Normal:
                    return 1f;
                case TimeSpeed.Fast:
                    return 3f;
                case TimeSpeed.Superfast:
                    if (nothingHappeningCached)
                        return 12f;
                    return 6f;
                case TimeSpeed.Ultrafast:
                    return 15f;
                default:
                    return -1f;
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => timeSpeedInt;
            set => timeSpeedInt = value;
        }

        public float RealTimeToTickThrough { get; set; }

        public Queue<ScheduledCommand> Cmds => cmds;

        public void Tick()
        {
            tickingMap = map;
            PreContext();

            //SimpleProfiler.Start();

            try
            {
                map.MapPreTick();
                mapTicks++;
                Find.TickManager.ticksGameInt = mapTicks;

                tickListNormal.Tick();
                tickListRare.Tick();
                tickListLong.Tick();

                TickMapTrading();

                storyteller.StorytellerTick();
                storyWatcher.StoryWatcherTick();

                map.MapPostTick();

                UpdateManagers();
                CacheNothingHappening();
            }
            finally
            {
                PostContext();

                Multiplayer.game.sync.TryAddMapRandomState(map.uniqueID, randState);

                tickingMap = null;

                //SimpleProfiler.Pause();
            }
        }

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            var data = new ByteReader(cmd.data);
            var context = data.MpContext();

            var cmdType = cmd.type;

            keepTheMap = false;
            var prevMap = Current.Game.CurrentMap;
            Current.Game.currentMapIndex = (sbyte) map.Index;

            executingCmdMap = map;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && !TickPatch.Skipping;

            PreContext();
            map.PushFaction(cmd.GetFaction());

            context.map = map;

            var prevSelected = Find.Selector.selected;
            Find.Selector.selected = new List<object>();

            SelectorDeselectPatch.deselected = new List<object>();

            var prevDevMode = Prefs.data.devMode;
            Prefs.data.devMode = Multiplayer.WorldComp.debugMode;

            try
            {
                if (cmdType == CommandType.Sync) Sync.HandleCmd(data);

                if (cmdType == CommandType.DebugTools) MpDebugTools.HandleCmd(data);

                if (cmdType == CommandType.CreateMapFactionData) HandleMapFactionData(cmd, data);

                if (cmdType == CommandType.MapTimeSpeed && Multiplayer.WorldComp.asyncTime)
                {
                    var speed = (TimeSpeed) data.ReadByte();
                    TimeSpeed = speed;

                    MpLog.Log("Set map time speed " + speed);
                }

                if (cmdType == CommandType.MapIdBlock)
                {
                    var block = IdBlock.Deserialize(data);

                    if (map != null)
                    {
                        //map.MpComp().mapIdBlock = block;
                    }
                }

                if (cmdType == CommandType.Designator) HandleDesignator(cmd, data);

                if (cmdType == CommandType.SpawnPawn)
                {
                    /*Pawn pawn = ScribeUtil.ReadExposable<Pawn>(data.ReadPrefixedBytes());

                    IntVec3 spawn = CellFinderLoose.TryFindCentralCell(map, 7, 10, (IntVec3 x) => !x.Roofed(map));
                    GenSpawn.Spawn(pawn, spawn, map);
                    Log.Message("spawned " + pawn);*/
                }

                if (cmdType == CommandType.Forbid)
                {
                    //HandleForbid(cmd, data);
                }

                UpdateManagers();
            }
            catch (Exception e)
            {
                Log.Error($"Map cmd exception ({cmdType}): {e}");
            }
            finally
            {
                Prefs.data.devMode = prevDevMode;

                foreach (var deselected in SelectorDeselectPatch.deselected)
                    prevSelected.Remove(deselected);
                SelectorDeselectPatch.deselected = null;

                Find.Selector.selected = prevSelected;

                map.PopFaction();
                PostContext();

                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdMap = null;

                if (!keepTheMap)
                    TrySetCurrentMap(prevMap);

                keepTheMap = false;

                Multiplayer.game.sync.TryAddCommandRandomState(randState);
            }
        }

        public void TickMapTrading()
        {
            var trading = Multiplayer.WorldComp.trading;

            for (var i = trading.Count - 1; i >= 0; i--)
            {
                var session = trading[i];
                if (session.playerNegotiator.Map != map) continue;

                if (session.ShouldCancel())
                {
                    Multiplayer.WorldComp.RemoveTradeSession(session);
                }
            }
        }

        // These are normally called in Map.MapUpdate() and react to changes in the game state even when the game is paused (not ticking)
        // Update() methods are not deterministic, but in multiplayer all game state changes (which don't happen during ticking) happen in commands
        // Thus these methods can be moved to Tick() and ExecuteCmd()
        public void UpdateManagers()
        {
            map.regionGrid.UpdateClean();
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();

            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            map.glowGrid.GlowGridUpdate_First();
        }

        public void PreContext()
        {
            //map.PushFaction(map.ParentFaction);

            prevTime = TimeSnapshot.GetAndSetFromMap(map);

            prevStoryteller = Current.Game.storyteller;
            prevStoryWatcher = Current.Game.storyWatcher;

            Current.Game.storyteller = storyteller;
            Current.Game.storyWatcher = storyWatcher;

            //UniqueIdsPatch.CurrentBlock = map.MpComp().mapIdBlock;
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;

            Rand.PushState();
            Rand.StateCompressed = randState;

            // Reset the effects of SkyManager.SkyManagerUpdate
            map.skyManager.curSkyGlowInt = map.skyManager.CurrentSkyTarget().glow;
        }

        public void PostContext()
        {
            UniqueIdsPatch.CurrentBlock = null;

            Current.Game.storyteller = prevStoryteller;
            Current.Game.storyWatcher = prevStoryWatcher;

            prevTime?.Set();

            randState = Rand.StateCompressed;
            Rand.PopState();

            //map.PopFaction();
        }

        public void FinalizeInit()
        {
            cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(map.uniqueID) ?? new List<ScheduledCommand>());
            Log.Message($"Init map with cmds {cmds.Count}");
        }

        private static void TrySetCurrentMap(Map map)
        {
            if (!Find.Maps.Contains(map))
            {
                if (Find.Maps.Any())
                    Current.Game.CurrentMap = Find.Maps[0];
                else
                    Current.Game.CurrentMap = null;
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
            }
            else
            {
                Current.Game.currentMapIndex = (sbyte) map.Index;
            }
        }

        private void HandleForbid(ScheduledCommand cmd, ByteReader data)
        {
            var thingId = data.ReadInt32();
            var value = data.ReadBool();

            var thing = map.listerThings.AllThings.Find(t => t.thingIDNumber == thingId) as ThingWithComps;
            if (thing == null) return;

            var forbiddable = thing.GetComp<CompForbiddable>();
            if (forbiddable == null) return;

            forbiddable.Forbidden = value;
        }

        private void HandleMapFactionData(ScheduledCommand cmd, ByteReader data)
        {
            var factionId = data.ReadInt32();

            var faction = Find.FactionManager.GetById(factionId);
            var comp = map.MpComp();

            if (!comp.factionMapData.ContainsKey(factionId))
            {
                var factionMapData = FactionMapData.New(factionId, map);
                comp.factionMapData[factionId] = factionMapData;

                factionMapData.areaManager.AddStartingAreas();
                map.pawnDestinationReservationManager.RegisterFaction(faction);

                MpLog.Log($"New map faction data for {faction.GetUniqueLoadID()}");
            }
        }

        private void HandleDesignator(ScheduledCommand command, ByteReader data)
        {
            var mode = Sync.ReadSync<DesignatorMode>(data);
            var designator = Sync.ReadSync<Designator>(data);
            if (designator == null) return;

            try
            {
                if (!SetDesignatorState(designator, data)) return;

                if (mode == DesignatorMode.SingleCell)
                {
                    var cell = Sync.ReadSync<IntVec3>(data);

                    designator.DesignateSingleCell(cell);
                    designator.Finalize(true);
                }
                else if (mode == DesignatorMode.MultiCell)
                {
                    var cells = Sync.ReadSync<IntVec3[]>(data);

                    designator.DesignateMultiCell(cells);
                }
                else if (mode == DesignatorMode.Thing)
                {
                    var thing = Sync.ReadSync<Thing>(data);
                    if (thing == null) return;

                    designator.DesignateThing(thing);
                    designator.Finalize(true);
                }
            }
            finally
            {
                DesignatorInstallPatch.thingToInstall = null;
            }
        }

        private bool SetDesignatorState(Designator designator, ByteReader data)
        {
            if (designator is Designator_AreaAllowed)
            {
                var area = Sync.ReadSync<Area>(data);
                if (area == null) return false;
                Designator_AreaAllowed.selectedArea = area;
            }

            if (designator is Designator_Place place) place.placingRot = Sync.ReadSync<Rot4>(data);

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                var stuffDef = Sync.ReadSync<ThingDef>(data);
                if (stuffDef == null) return false;
                build.stuffDef = stuffDef;
            }

            if (designator is Designator_Install)
            {
                var thing = Sync.ReadSync<Thing>(data);
                if (thing == null) return false;
                DesignatorInstallPatch.thingToInstall = thing;
            }

            if (designator is Designator_Zone)
            {
                var zone = Sync.ReadSync<Zone>(data);
                if (zone != null)
                    Find.Selector.selected.Add(zone);
            }

            return true;
        }

        private void CacheNothingHappening()
        {
            nothingHappeningCached = true;
            var list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);

            for (var j = 0; j < list.Count; j++)
            {
                var pawn = list[j];
                if (pawn.HostFaction == null && pawn.RaceProps.Humanlike && pawn.Awake())
                    nothingHappeningCached = false;
            }

            if (nothingHappeningCached && map.IsPlayerHome && map.dangerWatcher.DangerRating >= StoryDanger.Low)
                nothingHappeningCached = false;
        }

        public override string ToString()
        {
            return $"{nameof(MapAsyncTimeComp)}_{map}";
        }
    }

    public enum DesignatorMode : byte
    {
        SingleCell,
        MultiCell,
        Thing
    }
}