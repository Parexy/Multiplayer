﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Harmony;
using Multiplayer.Client.Synchronization;
using Multiplayer.Common;
using Multiplayer.Common.Networking;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems))]
    [HotSwappable]
    internal static class MpDebugTools
    {
        public static int currentPlayer;
        public static int currentHash;

        private static void Postfix(Dialog_DebugActionsMenu __instance)
        {
            var menu = __instance;

            if (MpVersion.IsDebug)
            {
                menu.DoLabel("Entry tools");
                menu.DebugAction("Entry action", EntryAction);
            }

            if (Current.ProgramState != ProgramState.Playing)
                return;

            menu.DoLabel("Local");

            menu.DebugAction("Save game", SaveGameLocal);
            menu.DebugAction("Print static fields", PrintStaticFields);

            if (MpVersion.IsDebug)
            {
                menu.DebugAction("Queue incident", QueueIncident);
                menu.DebugAction("Blocking long event", BlockingLongEvent);
            }

            if (Multiplayer.Client == null) return;

            if (MpVersion.IsDebug)
            {
                menu.DoLabel("Multiplayer");

                menu.DebugAction("Save game for everyone", SaveGameCmd);
                menu.DebugAction("Advance time", AdvanceTime);
                if (Multiplayer.game?.sync?.currentOpinion != null)
                    menu.DebugAction("Force Desync", ForceDesync);
            }
        }

        //No sync method here :)
        private static void ForceDesync()
        {
            Multiplayer.game.sync.TryAddStackTraceForDesyncLog("DebugDesync");
            Multiplayer.game.sync.currentOpinion.GetRandomStatesForMap(0).Add((uint) Rand.Value >> 32);
        }

        public static void EntryAction()
        {
            Log.Message(
                GenDefDatabase.GetAllDefsInDatabaseForDef(typeof(TerrainDef))
                    .Select(def => $"{def.modContentPack?.Name} {def} {def.shortHash} {def.index}")
                    .Join(delimiter: "\n")
            );
        }

        [SyncMethod]
        [SyncDebugOnly]
        private static void SaveGameCmd()
        {
            var map = Find.Maps[0];
            var mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }

        [SyncMethod]
        [SyncDebugOnly]
        private static void AdvanceTime()
        {
            File.WriteAllLines($"{Multiplayer.username}_all_static.txt", new[] {AllModStatics()});

            var to = 322 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                //Find.TickManager.ticksGameInt = to;
                //Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        private static void SaveGameLocal()
        {
            var data = ScribeUtil.WriteExposable(Current.Game, "game", true);
            File.WriteAllBytes($"game_0_{Multiplayer.username}.xml", data);
        }

        private static void PrintStaticFields()
        {
            Log.Message(StaticFieldsToString(typeof(Game).Assembly, type => type.Namespace.StartsWith("RimWorld") || type.Namespace.StartsWith("Verse")));
        }

        public static string AllModStatics()
        {
            var builder = new StringBuilder();

            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                builder.AppendLine("======== ").Append(mod.Name).AppendLine();
                foreach (var asm in mod.assemblies.loadedAssemblies) builder.AppendLine(StaticFieldsToString(asm, t => !t.Namespace.StartsWith("Harmony") && !t.Namespace.StartsWith("Multiplayer")));
            }

            return builder.ToString();
        }

        public static string StaticFieldsToString(Assembly asm, Predicate<Type> typeValidator)
        {
            var builder = new StringBuilder();

            object FieldValue(FieldInfo field)
            {
                var value = field.GetValue(null);
                if (value is ICollection col)
                    return col.Count;
                if (field.Name.ToLowerInvariant().Contains("path") && value is string path && (path.Contains("/") || path.Contains("\\")))
                    return "[x]";
                return value;
            }

            foreach (var type in asm.GetTypes())
                if (!type.IsGenericTypeDefinition && type.Namespace != null && typeValidator(type) && !type.HasAttribute<DefOf>() && !type.HasAttribute<CompilerGeneratedAttribute>())
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (!field.IsLiteral && !field.IsInitOnly && !field.HasAttribute<CompilerGeneratedAttribute>())
                            builder.AppendLine($"{field.FieldType} {type}::{field.Name}: {FieldValue(field)}");

            return builder.ToString();
        }

        private static void QueueIncident()
        {
            Find.Storyteller.incidentQueue.Add(IncidentDefOf.TraderCaravanArrival, Find.TickManager.TicksGame + 600, new IncidentParms {target = Find.CurrentMap});
        }

        private static void BlockingLongEvent()
        {
            LongEventHandler.QueueLongEvent(() => Thread.Sleep(60 * 1000), "Blocking", false, null);
        }

        public static void HandleCmd(ByteReader data)
        {
            currentPlayer = data.ReadInt32();
            var source = (DebugSource) data.ReadInt32();
            var cursorX = data.ReadInt32();
            var cursorZ = data.ReadInt32();

            if (Multiplayer.MapContext != null)
                MouseCellPatch.result = new IntVec3(cursorX, 0, cursorZ);
            else
                MouseTilePatch.result = cursorX;

            currentHash = data.ReadInt32();
            var state = Multiplayer.game.playerDebugState.GetOrAddNew(currentPlayer);

            var prevTool = DebugTools.curTool;
            DebugTools.curTool = state.tool;

            var prevSelected = Find.Selector.selected;
            var prevWorldSelected = Find.WorldSelector.selected;

            Find.Selector.selected = new List<object>();
            Find.WorldSelector.selected = new List<WorldObject>();

            var selectedId = data.ReadInt32();

            if (Multiplayer.MapContext != null)
            {
                var thing = ThingsById.thingsById.GetValueSafe(selectedId);
                if (thing != null)
                    Find.Selector.selected.Add(thing);
            }
            else
            {
                var obj = Find.WorldObjects.AllWorldObjects.FirstOrDefault(w => w.ID == selectedId);
                if (obj != null)
                    Find.WorldSelector.selected.Add(obj);
            }

            Multiplayer.game.sync.TryAddStackTraceForDesyncLog($"Executing debug tool {source}");
            Log.Message($"Debug tool {source} ({cursorX}, {cursorZ}) {currentHash}");

            try
            {
                if (source == DebugSource.ListingMap)
                {
                    new Dialog_DebugActionsMenu().DoListingItems_MapActions();
                    new Dialog_DebugActionsMenu().DoListingItems_MapTools();
                }
                else if (source == DebugSource.ListingWorld)
                {
                    new Dialog_DebugActionsMenu().DoListingItems_World();
                }
                else if (source == DebugSource.ListingPlay)
                {
                    new Dialog_DebugActionsMenu().DoListingItems_AllModePlayActions();
                }
                else if (source == DebugSource.Lister)
                {
                    var options = state.window as List<DebugMenuOption> ?? new List<DebugMenuOption>();
                    new Dialog_DebugOptionListLister(options).DoListingItems();
                }
                else if (source == DebugSource.Tool)
                {
                    DebugTools.curTool?.clickAction();
                }
                else if (source == DebugSource.FloatMenu)
                {
                    (state.window as List<FloatMenuOption>)?.FirstOrDefault(o => o.Hash() == currentHash)?.action();
                }
            }
            finally
            {
                if (TickPatch.currentExecutingCmdIssuedBySelf && DebugTools.curTool != null && DebugTools.curTool != state.tool)
                {
                    var map = Multiplayer.MapContext;
                    prevTool = new DebugTool(DebugTools.curTool.label, () => { SendCmd(DebugSource.Tool, 0, map); }, DebugTools.curTool.onGUIAction);
                }

                state.tool = DebugTools.curTool;
                DebugTools.curTool = prevTool;

                MouseCellPatch.result = null;
                MouseTilePatch.result = null;
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;
            }
        }

        public static void SendCmd(DebugSource source, int hash, Map map)
        {
            var writer = new ByteWriter();
            int cursorX = 0, cursorZ = 0;

            if (map != null)
            {
                cursorX = UI.MouseCell().x;
                cursorZ = UI.MouseCell().z;
            }
            else
            {
                cursorX = GenWorld.MouseTile();
            }

            writer.WriteInt32(Multiplayer.session.playerId);
            writer.WriteInt32((int) source);
            writer.WriteInt32(cursorX);
            writer.WriteInt32(cursorZ);
            writer.WriteInt32(hash);

            if (map != null)
                writer.WriteInt32(Find.Selector.SingleSelectedThing?.thingIDNumber ?? -1);
            else
                writer.WriteInt32(Find.WorldSelector.SingleSelectedObject?.ID ?? -1);

            var mapId = map?.uniqueID ?? ScheduledCommand.Global;

            Multiplayer.Client.SendCommand(CommandType.DebugTools, mapId, writer.ToArray());
        }

        public static DebugSource ListingSource()
        {
            if (ListingWorldMarker.drawing)
                return DebugSource.ListingWorld;
            if (ListingMapMarker.drawing)
                return DebugSource.ListingMap;
            if (ListingPlayMarker.drawing)
                return DebugSource.ListingPlay;

            return DebugSource.None;
        }
    }

    public class PlayerDebugState
    {
        public DebugTool tool;
        public object window;
    }

    public enum DebugSource
    {
        None,
        ListingWorld,
        ListingMap,
        ListingPlay,
        Lister,
        Tool,
        FloatMenu
    }

    [HarmonyPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems_AllModePlayActions))]
    internal static class ListingPlayMarker
    {
        public static bool drawing;

        private static void Prefix()
        {
            drawing = true;
        }

        private static void Postfix()
        {
            drawing = false;
        }
    }

    [HarmonyPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems_World))]
    internal static class ListingWorldMarker
    {
        public static bool drawing;

        private static void Prefix()
        {
            drawing = true;
        }

        private static void Postfix()
        {
            drawing = false;
        }
    }

    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoIncidentDebugAction))]
    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoIncidentWithPointsAction))]
    internal static class ListingIncidentMarker
    {
        public static IIncidentTarget target;

        private static void Prefix(IIncidentTarget target)
        {
            ListingIncidentMarker.target = target;
        }

        private static void Postfix()
        {
            target = null;
        }
    }

    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems_MapActions))]
    [MpPatch(typeof(Dialog_DebugActionsMenu), nameof(Dialog_DebugActionsMenu.DoListingItems_MapTools))]
    internal static class ListingMapMarker
    {
        public static bool drawing;

        private static void Prefix()
        {
            drawing = true;
        }

        private static void Postfix()
        {
            drawing = false;
        }
    }

    [MpPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DoGap))]
    [MpPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DoLabel))]
    internal static class CancelDebugDrawing
    {
        private static bool Prefix()
        {
            return !Multiplayer.ExecutingCmds;
        }
    }

    [HarmonyPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugAction))]
    [HotSwappable]
    internal static class DebugActionPatch
    {
        private static bool Prefix(Dialog_DebugOptionLister __instance, string label, ref Action action)
        {
            if (Multiplayer.Client == null) return true;
            if (Current.ProgramState == ProgramState.Playing && !Multiplayer.WorldComp.debugMode) return true;

            var originalAction = (action.Target as DebugListerContext)?.originalAction ?? action;

            var hash = Gen.HashCombineInt(
                GenText.StableStringHash(originalAction.Method.MethodDesc()),
                GenText.StableStringHash(label)
            );

            if (Multiplayer.ExecutingCmds)
            {
                if (hash == MpDebugTools.currentHash)
                    action();

                return false;
            }

            if (__instance is Dialog_DebugActionsMenu)
            {
                var source = MpDebugTools.ListingSource();
                if (source == DebugSource.None) return true;

                var map = source == DebugSource.ListingMap ? Find.CurrentMap : null;

                if (ListingIncidentMarker.target != null)
                    map = ListingIncidentMarker.target as Map;

                action = () => MpDebugTools.SendCmd(source, hash, map);
            }

            if (__instance is Dialog_DebugOptionListLister)
            {
                var context = (DebugListerContext) action.Target;
                action = () => MpDebugTools.SendCmd(DebugSource.Lister, hash, context.map);
            }

            return true;
        }
    }

    [MpPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolMap))]
    [MpPatch(typeof(Dialog_DebugOptionLister), nameof(Dialog_DebugOptionLister.DebugToolWorld))]
    internal static class DebugToolPatch
    {
        private static bool Prefix(Dialog_DebugOptionLister __instance, string label, Action toolAction, ref Container<DebugTool>? __state)
        {
            if (Multiplayer.Client == null) return true;
            if (Current.ProgramState == ProgramState.Playing && !Multiplayer.WorldComp.debugMode) return true;

            if (Multiplayer.ExecutingCmds)
            {
                var hash = Gen.HashCombineInt(GenText.StableStringHash(toolAction.Method.MethodDesc()), GenText.StableStringHash(label));
                if (hash == MpDebugTools.currentHash)
                    DebugTools.curTool = new DebugTool(label, toolAction);

                return false;
            }

            __state = DebugTools.curTool;

            return true;
        }

        private static void Postfix(Dialog_DebugOptionLister __instance, string label, Action toolAction, Container<DebugTool>? __state)
        {
            // New tool chosen
            if (__state != null && DebugTools.curTool != __state?.Inner)
            {
                var originalAction = (toolAction.Target as DebugListerContext)?.originalAction ?? toolAction;
                var hash = Gen.HashCombineInt(GenText.StableStringHash(originalAction.Method.MethodDesc()), GenText.StableStringHash(label));

                if (__instance is Dialog_DebugActionsMenu)
                {
                    var source = MpDebugTools.ListingSource();
                    if (source == DebugSource.None) return;

                    var map = source == DebugSource.ListingMap ? Find.CurrentMap : null;

                    MpDebugTools.SendCmd(source, hash, map);
                    DebugTools.curTool = null;
                }

                if (__instance is Dialog_DebugOptionListLister lister)
                {
                    var context = (DebugListerContext) toolAction.Target;
                    MpDebugTools.SendCmd(DebugSource.Lister, hash, context.map);
                    DebugTools.curTool = null;
                }
            }
        }
    }

    public class DebugListerContext
    {
        public Map map;
        public Action originalAction;

        public void Do()
        {
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class DebugListerAddPatch
    {
        private static bool Prefix(Window window)
        {
            if (Multiplayer.Client == null) return true;
            if (!Multiplayer.ExecutingCmds) return true;
            if (!Multiplayer.WorldComp.debugMode) return true;

            var keepOpen = TickPatch.currentExecutingCmdIssuedBySelf;
            var map = Multiplayer.MapContext;

            if (window is Dialog_DebugOptionListLister lister)
            {
                var options = lister.options;

                if (keepOpen)
                {
                    lister.options = new List<DebugMenuOption>();

                    foreach (var option in options)
                    {
                        var copy = option;
                        copy.method = new DebugListerContext {map = map, originalAction = copy.method}.Do;
                        lister.options.Add(copy);
                    }
                }

                Multiplayer.game.playerDebugState.GetOrAddNew(MpDebugTools.currentPlayer).window = options;
                return keepOpen;
            }

            if (window is FloatMenu menu)
            {
                var options = menu.options;

                if (keepOpen)
                {
                    menu.options = new List<FloatMenuOption>();

                    foreach (var option in options)
                    {
                        var copy = new FloatMenuOption(option.labelInt, option.action);
                        var hash = copy.Hash();
                        copy.action = () => MpDebugTools.SendCmd(DebugSource.FloatMenu, hash, map);
                        menu.options.Add(copy);
                    }
                }

                Multiplayer.game.playerDebugState.GetOrAddNew(MpDebugTools.currentPlayer).window = options;
                return keepOpen;
            }

            return true;
        }

        public static int Hash(this FloatMenuOption opt)
        {
            return Gen.HashCombineInt(GenText.StableStringHash(opt.action.Method.MethodDesc()), GenText.StableStringHash(opt.labelInt));
        }
    }
}