#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Sound;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.ReachedMaxMessagesLimit), MethodType.Getter)]
    internal static class LogMaxMessagesPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (MpVersion.IsDebug)
                __result = false;
        }
    }

    [MpPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenuMarker
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

    [HarmonyPatch(typeof(WildAnimalSpawner))]
    [HarmonyPatch(nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    public static class WildAnimalSpawnerTickMarker
    {
        public static bool ticking;

        private static void Prefix()
        {
            ticking = true;
        }

        private static void Postfix()
        {
            ticking = false;
        }
    }

    [HarmonyPatch(typeof(WildPlantSpawner))]
    [HarmonyPatch(nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    public static class WildPlantSpawnerTickMarker
    {
        public static bool ticking;

        private static void Prefix()
        {
            ticking = true;
        }

        private static void Postfix()
        {
            ticking = false;
        }
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects))]
    [HarmonyPatch(nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    public static class SteadyEnvironmentEffectsTickMarker
    {
        public static bool ticking;

        private static void Prefix()
        {
            ticking = true;
        }

        private static void Postfix()
        {
            ticking = false;
        }
    }

    [MpPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class MainMenu_AddHeight
    {
        private static void Prefix(ref Rect rect)
        {
            rect.height += 45f;
        }
    }

    [MpPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    [HotSwappable]
    public static class MainMenuPatch
    {
        private static void Prefix(Rect rect, List<ListableOption> optList)
        {
            if (!MainMenuMarker.drawing) return;

            if (Current.ProgramState == ProgramState.Entry)
            {
                var newColony = optList.FindIndex(opt => opt.label == "NewColony".Translate());
                if (newColony != -1)
                    optList.Insert(newColony + 1, new ListableOption("Multiplayer", () =>
                    {
                        if (Prefs.DevMode && Event.current.button == 1)
                            ShowModDebugInfo();
                        else
                            Find.WindowStack.Add(new ServerBrowser());
                    }));
            }

            if (optList.Any(opt => opt.label == "ReviewScenario".Translate()))
            {
                if (Multiplayer.session == null)
                    optList.Insert(0,
                        new ListableOption("MpHostServer".Translate(), () => Find.WindowStack.Add(new HostWindow())));

                if (MpVersion.IsDebug && Multiplayer.IsReplay)
                    optList.Insert(0,
                        new ListableOption("MpHostServer".Translate(),
                            () => Find.WindowStack.Add(new HostWindow(withSimulation: true))));

                if (Multiplayer.Client != null)
                {
                    if (!Multiplayer.IsReplay)
                        optList.Insert(0,
                            new ListableOption("MpSaveReplay".Translate(),
                                () => Find.WindowStack.Add(new Dialog_SaveReplay())));
                    else
                        optList.Insert(0, new ListableOption("MpConvert".Translate(), ConvertToSingleplayer));

                    optList.RemoveAll(opt => opt.label == "Save".Translate() || opt.label == "LoadGame".Translate());

                    var quitMenuLabel = "QuitToMainMenu".Translate();
                    var saveAndQuitMenu = "SaveAndQuitToMainMenu".Translate();
                    var quitMenu = optList.Find(opt => opt.label == quitMenuLabel || opt.label == saveAndQuitMenu);

                    if (quitMenu != null)
                    {
                        quitMenu.label = quitMenuLabel;
                        quitMenu.action = AskQuitToMainMenu;
                    }

                    var quitOSLabel = "QuitToOS".Translate();
                    var saveAndQuitOSLabel = "SaveAndQuitToOS".Translate();
                    var quitOS = optList.Find(opt => opt.label == quitOSLabel || opt.label == saveAndQuitOSLabel);

                    if (quitOS != null)
                    {
                        quitOS.label = quitOSLabel;
                        quitOS.action = () =>
                        {
                            if (Multiplayer.LocalServer != null)
                                Find.WindowStack.Add(
                                    Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(),
                                        Root.Shutdown, true));
                            else
                                Root.Shutdown();
                        };
                    }
                }
            }
        }

        private static void ShowModDebugInfo()
        {
            var mods = LoadedModManager.RunningModsListForReading;

            DebugTables.MakeTablesDialog(
                mods.Select((mod, i) => i),
                new TableDataGetter<int>($"Mod name {new string(' ', 20)}", i => mods[i].Name),
                new TableDataGetter<int>($"Mod id {new string(' ', 20)}", i => mods[i].Identifier),
                new TableDataGetter<int>($"Assembly hash {new string(' ', 10)}",
                    i => Multiplayer.enabledModAssemblyHashes[i].assemblyHash),
                new TableDataGetter<int>($"XML hash {new string(' ', 10)}",
                    i => Multiplayer.enabledModAssemblyHashes[i].xmlHash),
                new TableDataGetter<int>($"About hash {new string(' ', 10)}",
                    i => Multiplayer.enabledModAssemblyHashes[i].aboutHash)
            );
        }

        public static void AskQuitToMainMenu()
        {
            if (Multiplayer.LocalServer != null)
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("MpServerCloseConfirmation".Translate(),
                    GenScene.GoToMainMenu, true));
            else
                GenScene.GoToMainMenu();
        }

        private static void ConvertToSingleplayer()
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                Find.GameInfo.permadeathMode = false;
                // todo handle the other faction def too
                Multiplayer.DummyFaction.def = FactionDefOf.Ancients;

                OnMainThread.StopMultiplayer();

                var doc = SaveLoad.SaveGame();
                MemoryUtility.ClearAllMapsAndWorld();

                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = "play";

                LoadPatch.gameToLoad = doc;
            }, "Play", "MpConverting", true, null);
        }
    }

    [MpPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    [MpPatch(typeof(Root), nameof(Root.Shutdown))]
    internal static class Shutdown_Quit_Patch
    {
        private static void Prefix()
        {
            OnMainThread.StopMultiplayer();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
    public static class JobTrackerStart
    {
        private static void Prefix(Pawn_JobTracker __instance, Job newJob, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.InInterface)
            {
                Log.Warning($"Started a job {newJob} on pawn {__instance.pawn} from the interface!");
                return;
            }

            var pawn = __instance.pawn;

            __instance.jobsGivenThisTick = 0;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        private static void Postfix(Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class JobTrackerEndCurrent
    {
        private static void Prefix(Pawn_JobTracker __instance, JobCondition condition, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            var pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        private static void Postfix(Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.CheckForJobOverride))]
    public static class JobTrackerOverride
    {
        private static void Prefix(Pawn_JobTracker __instance, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            var pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            ThingContext.Push(pawn);
            __state = pawn.Map;
        }

        private static void Postfix(Container<Map>? __state)
        {
            if (__state != null)
            {
                __state.PopFaction();
                ThingContext.Pop();
            }
        }
    }

    public static class ThingContext
    {
        private static readonly Stack<Pair<Thing, Map>> stack = new Stack<Pair<Thing, Map>>();

        static ThingContext()
        {
            stack.Push(new Pair<Thing, Map>(null, null));
        }

        public static Thing Current => stack.Peek().First;
        public static Pawn CurrentPawn => Current as Pawn;

        public static Map CurrentMap
        {
            get
            {
                var peek = stack.Peek();
                if (peek.First != null && peek.First.Map != peek.Second)
                    Log.ErrorOnce("Thing " + peek.First + " has changed its map!", peek.First.thingIDNumber ^ 57481021);
                return peek.Second;
            }
        }

        public static void Push(Thing t)
        {
            stack.Push(new Pair<Thing, Map>(t, t.Map));
        }

        public static void Pop()
        {
            stack.Pop();
        }
    }

    [HarmonyPatch(typeof(GameEnder))]
    [HarmonyPatch(nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class GameEnderPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [HarmonyPatch(typeof(UniqueIDsManager))]
    [HarmonyPatch(nameof(UniqueIDsManager.GetNextID))]
    public static class UniqueIdsPatch
    {
        private static IdBlock currentBlock;

        private static int localIds = -1;

        public static IdBlock CurrentBlock
        {
            get => currentBlock;

            set
            {
                if (value != null && currentBlock != null && currentBlock != value)
                    Log.Warning("Reassigning the current id block!");
                currentBlock = value;
            }
        }

        private static bool Prefix()
        {
            return Multiplayer.Client == null || !Multiplayer.InInterface;
        }

        private static void Postfix(ref int __result)
        {
            if (Multiplayer.Client == null) return;

            /*IdBlock currentBlock = CurrentBlock;
            if (currentBlock == null)
            {
                __result = localIds--;
                if (!Multiplayer.ShouldSync)
                    Log.Warning("Tried to get a unique id without an id block set!");
                return;
            }

            __result = currentBlock.NextId();*/

            if (Multiplayer.InInterface)
            {
                __result = localIds--;
            }
            else
            {
                __result = Multiplayer.GlobalIdBlock.NextId();

                if (MpVersion.IsDebug)
                    Multiplayer.game.sync.TryAddStackTrace();
            }

            //MpLog.Log("got new id " + __result);

            /*if (currentBlock.current > currentBlock.blockSize * 0.95f && !currentBlock.overflowHandled)
            {
                Multiplayer.Client.Send(Packets.Client_IdBlockRequest, CurrentBlock.mapId);
                currentBlock.overflowHandled = true;
            }*/
        }
    }

    [HarmonyPatch(typeof(PawnComponentsUtility))]
    [HarmonyPatch(nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class AddAndRemoveCompsPatch
    {
        private static void Prefix(Pawn pawn, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null || pawn.Faction == null) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        private static void Postfix(Pawn pawn, Container<Map>? __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch]
    public static class WidgetsResolveParsePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), nameof(Widgets.ResolveParseNow)).MakeGenericMethod(typeof(int));
        }

        // Fix input field handling
        private static void Prefix(bool force, ref int val, ref string buffer, ref string edited)
        {
            if (force)
                edited = Widgets.ToStringTypedIn(val);
        }
    }

    [HarmonyPatch(typeof(Dialog_BillConfig), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(Bill_Production), typeof(IntVec3)})]
    public static class DialogPatch
    {
        private static void Postfix(Dialog_BillConfig __instance)
        {
            __instance.absorbInputAroundWindow = false;
        }
    }

    [HarmonyPatch(typeof(ListerHaulables))]
    [HarmonyPatch(nameof(ListerHaulables.ListerHaulablesTick))]
    public static class HaulablesTickPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
        }
    }

    [HarmonyPatch(typeof(ResourceCounter))]
    [HarmonyPatch(nameof(ResourceCounter.ResourceCounterTick))]
    public static class ResourcesTickPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
        }
    }

    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.WindowsForcePause), MethodType.Getter)]
    public static class WindowsPausePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(AutoBuildRoofAreaSetter))]
    [HarmonyPatch(nameof(AutoBuildRoofAreaSetter.TryGenerateAreaNow))]
    public static class AutoRoofPatch
    {
        private static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room, ref Map __state)
        {
            if (Multiplayer.Client == null) return true;
            if (room.Dereferenced || room.TouchesMapEdge || room.RegionCount > 26 || room.CellCount > 320 ||
                room.RegionType == RegionType.Portal) return false;

            var map = room.Map;
            Faction faction = null;

            foreach (var cell in room.BorderCells)
            {
                var holder = cell.GetRoofHolderOrImpassable(map);
                if (holder == null || holder.Faction == null) continue;
                if (faction != null && holder.Faction != faction) return false;
                faction = holder.Faction;
            }

            if (faction == null) return false;

            map.PushFaction(faction);
            __state = map;

            return true;
        }

        private static void Postfix(ref Map __state)
        {
            if (__state != null)
                __state.PopFaction();
        }
    }

    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    internal static class DrawPosPatch
    {
        // Give the root position during ticking
        private static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.InInterface) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.ExposeData))]
    public static class PawnExposeDataFirst
    {
        public static Container<Map>? state;

        // Postfix so Thing's faction is already loaded
        private static void Postfix(Thing __instance)
        {
            if (!(__instance is Pawn)) return;
            if (Multiplayer.Client == null || __instance.Faction == null || Find.FactionManager == null ||
                Find.FactionManager.AllFactions.Count() == 0) return;

            ThingContext.Push(__instance);
            state = __instance.Map;
            __instance.Map.PushFaction(__instance.Faction);
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch(nameof(Pawn.ExposeData))]
    public static class PawnExposeDataLast
    {
        private static void Postfix()
        {
            if (PawnExposeDataFirst.state != null)
            {
                PawnExposeDataFirst.state.PopFaction();
                ThingContext.Pop();
                PawnExposeDataFirst.state = null;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker))]
    [HarmonyPatch(nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
    public static class AddRemoveNeeds
    {
        private static void Prefix(Pawn_NeedsTracker __instance)
        {
            //MpLog.Log("add remove needs {0} {1}", FactionContext.OfPlayer.ToString(), __instance.GetPropertyOrField("pawn"));
        }
    }

    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.PreDrawPosCalculation))]
    public static class PreDrawPosCalcPatch
    {
        private static void Prefix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Pause();
        }

        private static void Postfix()
        {
            //if (MapAsyncTimeComp.tickingMap != null)
            //    SimpleProfiler.Start();
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    public static class TickRatePatch
    {
        private static bool Prefix(TickManager __instance, ref float __result)
        {
            if (Multiplayer.Client == null) return true;

            if (__instance.CurTimeSpeed == TimeSpeed.Paused)
                __result = 0;
            else if (__instance.slower.ForcedNormalSpeed)
                __result = 1;
            else if (__instance.CurTimeSpeed == TimeSpeed.Fast)
                __result = 3;
            else if (__instance.CurTimeSpeed == TimeSpeed.Superfast)
                __result = 6;
            else
                __result = 1;

            return false;
        }
    }

    public static class ValueSavePatch
    {
        public static bool DoubleSave_Prefix(string label, ref double value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G17"));
            return false;
        }

        public static bool FloatSave_Prefix(string label, ref float value)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return true;
            Scribe.saver.WriteElement(label, value.ToString("G9"));
            return false;
        }
    }

    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch(nameof(Log.Warning))]
    public static class CrossRefWarningPatch
    {
        private static readonly Regex regex =
            new Regex(
                @"^Could not resolve reference to object with loadID ([\w.-]*) of type ([\w.<>+]*)\. Was it compressed away");

        public static bool ignore;

        // The only non-generic entry point during cross reference resolving
        private static bool Prefix(string text)
        {
            if (Multiplayer.Client == null || ignore) return true;

            ignore = true;

            var groups = regex.Match(text).Groups;
            if (groups.Count == 3)
            {
                var loadId = groups[1].Value;
                var typeName = groups[2].Value;
                // todo
                return false;
            }

            ignore = false;

            return true;
        }
    }

    [HarmonyPatch(typeof(UI), nameof(UI.MouseCell))]
    public static class MouseCellPatch
    {
        public static IntVec3? result;

        private static void Postfix(ref IntVec3 __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(GenWorld), nameof(GenWorld.MouseTile))]
    public static class MouseTilePatch
    {
        public static int? result;

        private static void Postfix(ref int __result)
        {
            if (result.HasValue)
                __result = result.Value;
        }
    }

    [HarmonyPatch(typeof(KeyBindingDef))]
    [HarmonyPatch(nameof(KeyBindingDef.IsDownEvent), MethodType.Getter)]
    public static class KeyIsDownPatch
    {
        public static bool? shouldQueue;

        private static bool Prefix(KeyBindingDef __instance)
        {
            return !(__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue);
        }

        private static void Postfix(KeyBindingDef __instance, ref bool __result)
        {
            if (__instance == KeyBindingDefOf.QueueOrder && shouldQueue.HasValue)
                __result = shouldQueue.Value;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class PawnSpawnSetupMarker
    {
        public static bool respawningAfterLoad;

        private static void Prefix(bool respawningAfterLoad)
        {
            PawnSpawnSetupMarker.respawningAfterLoad = respawningAfterLoad;
        }

        private static void Postfix()
        {
            respawningAfterLoad = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.ResetToCurrentPosition))]
    internal static class PatherResetPatch
    {
        private static bool Prefix()
        {
            return !PawnSpawnSetupMarker.respawningAfterLoad;
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    internal static class SetupQuickTestPatch
    {
        public static bool marker;

        private static void Prefix()
        {
            marker = true;
        }

        private static void Postfix()
        {
            if (MpVersion.IsDebug)
                Find.GameInitData.mapSize = 250;
            marker = false;
        }
    }

    [HarmonyPatch(typeof(GameInitData), nameof(GameInitData.ChooseRandomStartingTile))]
    internal static class RandomStartingTilePatch
    {
        private static void Postfix()
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
            {
                Find.GameInitData.startingTile = 501;
                Find.WorldGrid[Find.GameInitData.startingTile].hilliness = Hilliness.SmallHills;
            }
        }
    }

    [HarmonyPatch(typeof(GenText), nameof(GenText.RandomSeedString))]
    internal static class GrammarRandomStringPatch
    {
        private static void Postfix(ref string __result)
        {
            if (MpVersion.IsDebug && SetupQuickTestPatch.marker)
                __result = "multiplayer1";
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "<SortWornApparelIntoDrawOrder>m__0")]
    internal static class FixApparelSort
    {
        private static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [MpPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.GenerateStartingOutfits))]
    [MpPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.GenerateStartingDrugPolicies))]
    [MpPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.GenerateStartingFoodRestrictions))]
    internal static class CancelReinitializationDuringLoading
    {
        private static bool Prefix()
        {
            return Scribe.mode != LoadSaveMode.LoadingVars;
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit))]
    internal static class OutfitUniqueIdPatch
    {
        private static void Postfix(Outfit __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy))]
    internal static class DrugPolicyUniqueIdPatch
    {
        private static void Postfix(DrugPolicy __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction))]
    internal static class FoodRestrictionUniqueIdPatch
    {
        private static void Postfix(FoodRestriction __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.id = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.RebuildAll))]
    internal static class ListerFilthRebuildPatch
    {
        private static bool ignore;

        private static void Prefix(ListerFilthInHomeArea __instance)
        {
            if (Multiplayer.Client == null || ignore) return;

            ignore = true;
            foreach (var data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.RebuildAll();
                __instance.map.PopFaction();
            }

            ignore = false;
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthSpawned))]
    internal static class ListerFilthSpawnedPatch
    {
        private static bool ignore;

        private static void Prefix(ListerFilthInHomeArea __instance, Filth f)
        {
            if (Multiplayer.Client == null || ignore) return;

            ignore = true;
            foreach (var data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.Notify_FilthSpawned(f);
                __instance.map.PopFaction();
            }

            ignore = false;
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthDespawned))]
    internal static class ListerFilthDespawnedPatch
    {
        private static bool ignore;

        private static void Prefix(ListerFilthInHomeArea __instance, Filth f)
        {
            if (Multiplayer.Client == null || ignore) return;

            ignore = true;
            foreach (var data in __instance.map.MpComp().factionMapData.Values)
            {
                __instance.map.PushFaction(data.factionId);
                data.listerFilthInHomeArea.Notify_FilthDespawned(f);
                __instance.map.PopFaction();
            }

            ignore = false;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    internal static class LoadGameMarker
    {
        public static bool loading;

        private static void Prefix()
        {
            loading = true;
        }

        private static void Postfix()
        {
            loading = false;
        }
    }

    [MpPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    [MpPatch(typeof(Command_SetPlantToGrow), nameof(Command_SetPlantToGrow.WarnAsAppropriate))]
    [MpPatch(typeof(TutorUtility), nameof(TutorUtility.DoModalDialogIfNotKnown))]
    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TryHideWorld))]
    internal static class CancelFeedbackNotTargetedAtMe
    {
        public static bool Cancel =>
            Multiplayer.Client != null &&
            Multiplayer.ExecutingCmds &&
            !TickPatch.currentExecutingCmdIssuedBySelf;

        private static bool Prefix()
        {
            return !Cancel;
        }
    }

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.BeginTargeting), typeof(TargetingParameters),
        typeof(Action<LocalTargetInfo>), typeof(Pawn), typeof(Action), typeof(Texture2D))]
    internal static class CancelBeginTargeting
    {
        private static bool Prefix()
        {
            if (TickPatch.currentExecutingCmdIssuedBySelf && MapAsyncTimeComp.executingCmdMap != null)
                MapAsyncTimeComp.keepTheMap = true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote),
        new[] {typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(float)})]
    [MpPatch(typeof(MoteMaker), nameof(MoteMaker.MakeStaticMote),
        new[] {typeof(Vector3), typeof(Map), typeof(ThingDef), typeof(float)})]
    internal static class CancelMotesNotTargetedAtMe
    {
        private static bool Prefix(ThingDef moteDef)
        {
            if (moteDef == ThingDefOf.Mote_FeedbackGoto)
                return true;

            return !CancelFeedbackNotTargetedAtMe.Cancel;
        }
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] {typeof(Message), typeof(bool)})]
    internal static class SilenceMessagesNotTargetedAtMe
    {
        private static bool Prefix(bool historical)
        {
            var cancel = Multiplayer.Client != null && !historical && Multiplayer.ExecutingCmds &&
                         !TickPatch.currentExecutingCmdIssuedBySelf;
            return !cancel;
        }
    }

    [MpPatch(typeof(Messages), nameof(Messages.Message), new[] {typeof(string), typeof(MessageTypeDef), typeof(bool)})]
    [MpPatch(typeof(Messages), nameof(Messages.Message),
        new[] {typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool)})]
    internal static class MessagesMarker
    {
        public static bool? historical;

        private static void Prefix(bool historical)
        {
            MessagesMarker.historical = historical;
        }

        private static void Postfix()
        {
            historical = null;
        }
    }

    [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextMessageID))]
    internal static class NextMessageIdPatch
    {
        private static int nextUniqueUnhistoricalMessageId = -1;

        private static bool Prefix()
        {
            return !MessagesMarker.historical.HasValue || MessagesMarker.historical.Value;
        }

        private static void Postfix(ref int __result)
        {
            if (MessagesMarker.historical.HasValue && !MessagesMarker.historical.Value)
                __result = nextUniqueUnhistoricalMessageId--;
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    internal static class RootPlayStartMarker
    {
        public static bool starting;

        private static void Prefix()
        {
            starting = true;
        }

        private static void Postfix()
        {
            starting = false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[]
        {typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>)})]
    internal static class CancelRootPlayStartLongEvents
    {
        public static bool cancel;

        private static bool Prefix()
        {
            if (RootPlayStartMarker.starting && cancel) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.SetColor))]
    internal static class DisableScreenFade1
    {
        private static bool Prefix()
        {
            return !LongEventHandler.eventQueue.Any(e => e.eventTextKey == "MpLoading");
        }
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.StartFade))]
    internal static class DisableScreenFade2
    {
        private static bool Prefix()
        {
            return !LongEventHandler.eventQueue.Any(e => e.eventTextKey == "MpLoading");
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    internal static class TryGetMeleeVerbPatch
    {
        private static bool Cancel => Multiplayer.Client != null && Multiplayer.InInterface;

        private static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        private static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
                __result = __instance.GetUpdatedAvailableVerbsList(false)
                    .FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
        }
    }

    [HarmonyPatch(typeof(ThingGrid), nameof(ThingGrid.Register))]
    internal static class DontEnlistNonSaveableThings
    {
        private static bool Prefix(Thing t)
        {
            return t.def.isSaveable;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps))]
    [HarmonyPatch(nameof(ThingWithComps.InitializeComps))]
    internal static class InitializeCompsPatch
    {
        private static void Postfix(ThingWithComps __instance)
        {
            if (__instance is Pawn)
            {
                var comp = new MultiplayerPawnComp() {parent = __instance};
                __instance.AllComps.Add(comp);
            }
        }
    }

    public class MultiplayerPawnComp : ThingComp
    {
        public SituationalThoughtHandler thoughtsForInterface;
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RandomPreferredName))]
    internal static class PreferredNamePatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.TryGetRandomUnusedSolidName))]
    internal static class GenerateNewPawnInternalPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            var insts = new List<CodeInstruction>(e);

            insts.Insert(
                insts.Count - 1,
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle))
                        .MakeGenericMethod(typeof(NameTriple)))
            );

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            var iters = Rand.iterations;

            var i = 0;
            while (i < list.Count)
            {
                var index = Mathf.Abs(Rand.random.GetInt(iters--) % (i + 1));
                var value = list[index];
                list[index] = list[i];
                list[i] = value;
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(GlowGrid), MethodType.Constructor, new[] {typeof(Map)})]
    internal static class GlowGridCtorPatch
    {
        private static void Postfix(GlowGrid __instance)
        {
            __instance.litGlowers = new HashSet<CompGlower>(new CompGlowerEquality());
        }

        private class CompGlowerEquality : IEqualityComparer<CompGlower>
        {
            public bool Equals(CompGlower x, CompGlower y)
            {
                return x == y;
            }

            public int GetHashCode(CompGlower obj)
            {
                return obj.parent.thingIDNumber;
            }
        }
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    internal static class BeforeMapGeneration
    {
        private static void Prefix(ref Action<Map> extraInitBeforeContentGen)
        {
            if (Multiplayer.Client == null) return;
            extraInitBeforeContentGen += SetupMap;
        }

        private static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            Log.Message("Unique ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("Rand " + Rand.StateCompressed);
        }

        public static void SetupMap(Map map)
        {
            Log.Message("New map " + map.uniqueID);
            Log.Message("Uniq ids " + Multiplayer.GlobalIdBlock.current);
            Log.Message("Rand " + Rand.StateCompressed);

            var async = new MapAsyncTimeComp(map);
            Multiplayer.game.asyncTimeComps.Add(async);

            var mapComp = new MultiplayerMapComp(map);
            Multiplayer.game.mapComps.Add(mapComp);

            mapComp.factionMapData[Faction.OfPlayer.loadID] = FactionMapData.FromMap(map, Faction.OfPlayer.loadID);

            var dummyFaction = Multiplayer.DummyFaction;
            mapComp.factionMapData[dummyFaction.loadID] = FactionMapData.New(dummyFaction.loadID, map);
            mapComp.factionMapData[dummyFaction.loadID].areaManager.AddStartingAreas();

            async.mapTicks = Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ??
                             Find.TickManager.TicksGame;
            async.storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficulty);
            async.storyWatcher = new StoryWatcher();

            if (!Multiplayer.WorldComp.asyncTime)
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    internal static class CaravanVisibleToCameraPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!Multiplayer.InInterface)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class DisableCaravanSplit
    {
        private static bool Prefix(Window window)
        {
            if (Multiplayer.Client == null) return true;

            if (window is Dialog_Negotiation)
                return false;

            if (window is Dialog_SplitCaravan)
            {
                Messages.Message("MpNotAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }
    }

    [MpPatch(typeof(IncidentWorker_CaravanMeeting), nameof(IncidentWorker_CaravanMeeting.CanFireNowSub))]
    [MpPatch(typeof(IncidentWorker_CaravanDemand), nameof(IncidentWorker_CaravanDemand.CanFireNowSub))]
    [MpPatch(typeof(IncidentWorker_RansomDemand), nameof(IncidentWorker_RansomDemand.CanFireNowSub))]
    internal static class CancelIncidents
    {
        private static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(IncidentDef), nameof(IncidentDef.TargetAllowed))]
    internal static class GameConditionIncidentTargetPatch
    {
        private static void Postfix(IncidentDef __instance, IIncidentTarget target, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (__instance.workerClass == typeof(IncidentWorker_MakeGameCondition) ||
                __instance.workerClass == typeof(IncidentWorker_Aurora))
                __result = target.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_PlayerHome);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Aurora), nameof(IncidentWorker_Aurora.AuroraWillEndSoon))]
    internal static class IncidentWorkerAuroraPatch
    {
        private static void Postfix(Map map, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (map != Multiplayer.MapContext)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(NamePlayerFactionAndSettlementUtility),
        nameof(NamePlayerFactionAndSettlementUtility.CanNameAnythingNow))]
    internal static class NoNamingInMultiplayer
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TrySelect))]
    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect))]
    [MpPatch(typeof(CameraJumper), nameof(CameraJumper.TryJump), new[] {typeof(GlobalTargetInfo)})]
    internal static class NoCameraJumpingDuringSkipping
    {
        private static bool Prefix()
        {
            return !TickPatch.Skipping;
        }
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    internal static class WealthWatcherRecalc
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null || !Multiplayer.ShouldSync;
        }
    }

    internal static class CaptureThingSetMakers
    {
        public static List<ThingSetMaker> captured = new List<ThingSetMaker>();

        private static void Prefix(ThingSetMaker __instance)
        {
            if (Current.ProgramState == ProgramState.Entry)
                captured.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    internal static class FloodUnfogPatch
    {
        private static void Postfix(ref FloodUnfogResult __result)
        {
            if (Multiplayer.Client != null)
                __result.allOnScreen = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawTrackerTick))]
    internal static class DrawTrackerTickPatch
    {
        private static readonly MethodInfo CellRectContains =
            AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == CellRectContains)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
    internal static class ArchiveAddPatch
    {
        private static bool Prefix(IArchivable archivable)
        {
            if (Multiplayer.Client == null) return true;

            if (archivable is Message msg && msg.ID < 0)
                return false;
            else if (archivable is Letter letter && letter.ID < 0)
                return false;

            return true;
        }
    }

    // todo does this cause issues?
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetHashCode))]
    internal static class TradeableHashCode
    {
        private static bool Prefix()
        {
            return false;
        }

        private static void Postfix(Tradeable __instance, ref int __result)
        {
            __result = RuntimeHelpers.GetHashCode(__instance);
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[]
        {typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>)})]
    internal static class MarkLongEvents
    {
        private static readonly MethodInfo MarkerMethod = AccessTools.Method(typeof(MarkLongEvents), nameof(Marker));

        private static void Prefix(ref Action action)
        {
            if (Multiplayer.Client != null && (Multiplayer.Ticking || Multiplayer.ExecutingCmds)) action += Marker;
        }

        private static void Marker()
        {
        }

        public static bool IsTickMarked(Action action)
        {
            return (action as MulticastDelegate)?.GetInvocationList()?.Any(d => d.Method == MarkerMethod) ?? false;
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    internal static class NewLongEvent
    {
        public static bool currentEventWasMarked;

        private static void Prefix(ref bool __state)
        {
            __state = LongEventHandler.currentEvent == null;
            currentEventWasMarked = MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction);
        }

        private static void Postfix(bool __state)
        {
            currentEventWasMarked = false;

            if (Multiplayer.Client == null) return;

            if (__state && MarkLongEvents.IsTickMarked(LongEventHandler.currentEvent?.eventAction))
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] {true});
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteToExecuteWhenFinished))]
    internal static class LongEventEnd
    {
        private static void Postfix()
        {
            if (Multiplayer.Client != null && NewLongEvent.currentEventWasMarked)
                Multiplayer.Client.Send(Packets.Client_Pause, new object[] {false});
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[]
        {typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>)})]
    internal static class LongEventAlwaysSync
    {
        private static void Prefix(ref bool doAsynchronously)
        {
            if (Multiplayer.ExecutingCmds)
                doAsynchronously = false;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForWorker))]
    internal static class FindBestStorageCellMarker
    {
        public static bool executing;

        private static void Prefix()
        {
            executing = true;
        }

        private static void Postfix()
        {
            executing = false;
        }
    }

    [HarmonyPatch(typeof(RandomNumberGenerator_BasicHash), nameof(RandomNumberGenerator_BasicHash.GetHash))]
    internal static class RandGetHashPatch
    {
        private static void Postfix()
        {
            if (!MpVersion.IsDebug) return;

            if (Multiplayer.Client == null) return;
            if (Rand.stateStack.Count > 1) return;
            if (TickPatch.Skipping || Multiplayer.IsReplay) return;

            if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return;

            if (!WildAnimalSpawnerTickMarker.ticking &&
                !WildPlantSpawnerTickMarker.ticking &&
                !SteadyEnvironmentEffectsTickMarker.ticking &&
                !FindBestStorageCellMarker.executing &&
                ThingContext.Current?.def != ThingDefOf.SteamGeyser)
                Multiplayer.game.sync.TryAddStackTrace();
        }
    }

    [HarmonyPatch(typeof(Zone), nameof(Zone.Cells), MethodType.Getter)]
    internal static class ZoneCellsShufflePatch
    {
        private static readonly FieldInfo CellsShuffled = AccessTools.Field(typeof(Zone), nameof(Zone.cellsShuffled));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var found = false;

            foreach (var inst in insts)
            {
                yield return inst;

                if (!found && inst.operand == CellsShuffled)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(ZoneCellsShufflePatch), nameof(ShouldShuffle)));
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.Or);
                    found = true;
                }
            }
        }

        private static bool ShouldShuffle()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.StartOrResumeBillJob))]
    internal static class StartOrResumeBillPatch
    {
        private static readonly FieldInfo LastFailTicks =
            AccessTools.Field(typeof(Bill), nameof(Bill.lastIngredientSearchFailTicks));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase original)
        {
            var list = new List<CodeInstruction>(insts);

            int index = new CodeFinder(original, list).Forward(OpCodes.Stfld, LastFailTicks).Advance(-1);
            if (list[index].opcode != OpCodes.Ldc_I4_0)
                throw new Exception("Wrong code");

            list.RemoveAt(index);

            list.Insert(
                index,
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StartOrResumeBillPatch), nameof(Value)))
            );

            return list;
        }

        private static int Value(Bill bill, Pawn pawn)
        {
            return FloatMenuMakerMap.makingFor == pawn ? bill.lastIngredientSearchFailTicks : 0;
        }
    }

    [HarmonyPatch(typeof(Archive), "<Add>m__2")]
    internal static class SortArchivablesById
    {
        private static void Postfix(IArchivable x, ref int __result)
        {
            if (x is ArchivedDialog dialog)
                __result = dialog.ID;
            else if (x is Letter letter)
                __result = letter.ID;
            else if (x is Message msg)
                __result = msg.ID;
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    internal static class DangerRatingPatch
    {
        private static bool Prefix()
        {
            return !Multiplayer.InInterface;
        }

        private static void Postfix(DangerWatcher __instance, ref StoryDanger __result)
        {
            if (Multiplayer.InInterface)
                __result = __instance.dangerRatingInt;
        }
    }

    [HarmonyPatch(typeof(Selector), nameof(Selector.Deselect))]
    internal static class SelectorDeselectPatch
    {
        public static List<object> deselected;

        private static void Prefix(object obj)
        {
            if (deselected != null)
                deselected.Add(obj);
        }
    }

    [HarmonyPatch(typeof(DirectXmlSaver), nameof(DirectXmlSaver.XElementFromObject), typeof(object), typeof(Type),
        typeof(string), typeof(FieldInfo), typeof(bool))]
    internal static class ExtendDirectXmlSaver
    {
        public static bool extend;

        private static bool Prefix(object obj, Type expectedType, string nodeName, FieldInfo owningField,
            ref XElement __result)
        {
            if (!extend) return true;
            if (obj == null) return true;

            if (obj is Array arr)
            {
                var elementType = arr.GetType().GetElementType();
                var listType = typeof(List<>).MakeGenericType(elementType);
                __result = DirectXmlSaver.XElementFromObject(Activator.CreateInstance(listType, arr), listType,
                    nodeName, owningField);
                return false;
            }

            string content = null;

            if (obj is Type type)
                content = type.FullName;
            else if (obj is MethodBase method)
                content = method.MethodDesc();
            else if (obj is Delegate del)
                content = del.Method.MethodDesc();

            if (content != null)
            {
                __result = new XElement(nodeName, content);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Pause))]
    internal static class TickManagerPausePatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [HarmonyPatch(typeof(WorldRoutePlanner), nameof(WorldRoutePlanner.ShouldStop), MethodType.Getter)]
    internal static class RoutePlanner_ShouldStop_Patch
    {
        private static void Postfix(WorldRoutePlanner __instance, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            // Ignore pause
            if (__result && __instance.active && WorldRendererUtility.WorldRenderedNow)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.ImmobilizedByMass), MethodType.Getter)]
    internal static class ImmobilizedByMass_Patch
    {
        private static bool Prefix()
        {
            return !Multiplayer.InInterface;
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    internal static class FactionCallNotice
    {
        private static void Postfix(ref IEnumerable<FloatMenuOption> __result)
        {
            if (Multiplayer.Client != null)
                __result = __result.Concat(new FloatMenuOption("MpCallingFactionNotAvailable".Translate(), null));
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    internal static class CancelSyncDuringPawnGeneration
    {
        private static void Prefix()
        {
            Multiplayer.dontSync = true;
        }

        private static void Postfix()
        {
            Multiplayer.dontSync = false;
        }
    }

    [HarmonyPatch(typeof(StoryWatcher_PopAdaptation), nameof(StoryWatcher_PopAdaptation.Notify_PawnEvent))]
    internal static class CancelStoryWatcherEventInInterface
    {
        private static bool Prefix()
        {
            return !Multiplayer.InInterface;
        }
    }

    [HarmonyPatch(typeof(DesignationDragger), nameof(DesignationDragger.UpdateDragCellsIfNeeded))]
    internal static class CancelUpdateDragCellsIfNeeded
    {
        private static bool Prefix()
        {
            return !Multiplayer.ExecutingCmds;
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    internal static class WorkPrioritySameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        private static bool Prefix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            return __instance.GetPriority(w) != priority;
        }
    }

    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestriction), MethodType.Setter)]
    internal static class AreaRestrictionSameValue
    {
        [HarmonyPriority(MpPriority.MpFirst + 1)]
        private static bool Prefix(Pawn_PlayerSettings __instance, Area value)
        {
            return __instance.AreaRestriction != value;
        }
    }

    [MpPatch(typeof(GlobalTargetInfo), nameof(GlobalTargetInfo.GetHashCode))]
    [MpPatch(typeof(TargetInfo), nameof(TargetInfo.GetHashCode))]
    internal static class PatchTargetInfoHashCodes
    {
        private static readonly MethodInfo Combine =
            AccessTools.Method(typeof(Gen), nameof(Gen.HashCombine)).MakeGenericMethod(typeof(Map));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand == Combine)
                    inst.operand = AccessTools.Method(typeof(PatchTargetInfoHashCodes), nameof(CombineHashes));

                yield return inst;
            }
        }

        private static int CombineHashes(int seed, Map map)
        {
            return Gen.HashCombineInt(seed, map.uniqueID);
        }
    }
}