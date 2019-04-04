#region

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Xml;
using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;

#endregion

namespace Multiplayer.Client
{
    public static class SaveLoad
    {
        public static XmlDocument SaveAndReload()
        {
            Multiplayer.reloading = true;

            WorldGrid worldGridSaved = Find.WorldGrid;
            WorldRenderer worldRendererSaved = Find.World.renderer;
            Dictionary<int, Vector3> tweenedPos = new Dictionary<int, Vector3>();
            Dictionary<int, MapDrawer> drawers = new Dictionary<int, MapDrawer>();
            int localFactionId = Multiplayer.RealPlayerFaction.loadID;
            Dictionary<int, Queue<ScheduledCommand>> mapCmds = new Dictionary<int, Queue<ScheduledCommand>>();
            WorldRenderMode planetRenderMode = Find.World.renderer.wantedMode;
            ChatWindow chatWindow = ChatWindow.Opened;

            ByteWriter selectedData = new ByteWriter();
            Sync.WriteSync(selectedData, Find.Selector.selected.OfType<ISelectable>().ToList());

            //RealPlayerFaction = DummyFaction;

            foreach (Map map in Find.Maps)
            {
                drawers[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;

                mapCmds[map.uniqueID] = map.AsyncTime().cmds;
            }

            mapCmds[ScheduledCommand.Global] = Multiplayer.WorldComp.cmds;

            Stopwatch watch = Stopwatch.StartNew();
            XmlDocument gameDoc = SaveGame();
            Log.Message("Saving took " + watch.ElapsedMilliseconds);

            MapDrawerRegenPatch.copyFrom = drawers;
            WorldGridCachePatch.copyFrom = worldGridSaved;
            WorldRendererCachePatch.copyFrom = worldRendererSaved;

            LoadInMainThread(gameDoc);

            Multiplayer.RealPlayerFaction = Find.FactionManager.GetById(localFactionId);

            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                    if (tweenedPos.TryGetValue(p.thingIDNumber, out Vector3 v))
                    {
                        p.drawer.tweener.tweenedPos = v;
                        p.drawer.tweener.lastDrawFrame = Time.frameCount;
                    }

                m.AsyncTime().cmds = mapCmds[m.uniqueID];
            }

            if (chatWindow != null)
                Find.WindowStack.Add_KeepRect(chatWindow);

            ByteReader selectedReader = new ByteReader(selectedData.ToArray())
                {context = new MpContext {map = Find.CurrentMap}};
            Find.Selector.selected = Sync.ReadSync<List<ISelectable>>(selectedReader).NotNull().Cast<object>().ToList();

            Find.World.renderer.wantedMode = planetRenderMode;
            Multiplayer.WorldComp.cmds = mapCmds[ScheduledCommand.Global];

            Multiplayer.reloading = false;

            return gameDoc;
        }

        public static void LoadInMainThread(XmlDocument gameDoc)
        {
            Stopwatch watch = Stopwatch.StartNew();

            ClearState();
            MemoryUtility.ClearAllMapsAndWorld();

            LoadPatch.gameToLoad = gameDoc;

            CancelRootPlayStartLongEvents.cancel = true;
            Find.Root.Start();
            CancelRootPlayStartLongEvents.cancel = false;

            //foreach (var alert in ((UIRoot_Play)Find.UIRoot).alerts.AllAlerts)
            //    alert.lastBellTime = float.NaN;

            // SaveCompression enabled in the patch
            SavedGameLoaderNow.LoadGameFromSaveFileNow(null);

            Log.Message("Loading took " + watch.ElapsedMilliseconds);
        }

        private static void ClearState()
        {
            if (Find.MusicManagerPlay != null)
            {
                // todo destroy other game objects?
                Object.Destroy(Find.MusicManagerPlay.audioSource.gameObject);
                Object.Destroy(Find.SoundRoot.sourcePool.sourcePoolCamera.cameraSourcesContainer);
                Object.Destroy(Find.SoundRoot.sourcePool.sourcePoolWorld.sourcesWorld[0].gameObject);

                foreach (Sustainer sustainer in Find.SoundRoot.sustainerManager.AllSustainers.ToList())
                    sustainer.Cleanup();
            }
        }

        public static XmlDocument SaveGame()
        {
            SaveCompression.doSaveCompression = true;

            ScribeUtil.StartWritingToDoc();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            int currentMapIndex = Current.Game.currentMapIndex;
            Scribe_Values.Look(ref currentMapIndex, "currentMapIndex", -1);
            Current.Game.ExposeSmallComponents();
            World world = Current.Game.World;
            Scribe_Deep.Look(ref world, "world");
            List<Map> maps = Find.Maps;
            Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
            Find.CameraDriver.Expose();
            Scribe.ExitNode();

            SaveCompression.doSaveCompression = false;

            return ScribeUtil.FinishWritingToDoc();
        }

        public static void CacheGameData(XmlDocument doc)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            OnMainThread.cachedMapData.Clear();
            OnMainThread.cachedMapCmds.Clear();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                OnMainThread.cachedMapData[id] = mapData;
                OnMainThread.cachedMapCmds[id] =
                    new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            gameNode["currentMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(doc);
            OnMainThread.cachedAtTime = TickPatch.Timer;
            OnMainThread.cachedGameData = gameData;
            OnMainThread.cachedMapCmds[ScheduledCommand.Global] =
                new List<ScheduledCommand>(Multiplayer.WorldComp.cmds);
        }

        public static void SendCurrentGameData(bool async)
        {
            Dictionary<int, byte[]> mapsData = new Dictionary<int, byte[]>(OnMainThread.cachedMapData);
            byte[] gameData = OnMainThread.cachedGameData;

            void Send()
            {
                ByteWriter writer = new ByteWriter();

                writer.WriteInt32(mapsData.Count);
                foreach (KeyValuePair<int, byte[]> mapData in mapsData)
                {
                    writer.WriteInt32(mapData.Key);
                    writer.WritePrefixedBytes(GZipStream.CompressBuffer(mapData.Value));
                }

                writer.WritePrefixedBytes(GZipStream.CompressBuffer(gameData));

                byte[] data = writer.ToArray();

                OnMainThread.Enqueue(() => Multiplayer.Client.SendFragmented(Packets.ClientAutosavedData, data));
            }

            if (async)
                ThreadPool.QueueUserWorkItem(c => Send());
            else
                Send();
        }
    }

    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch(nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow))]
    [HarmonyPatch(new[] {typeof(string)})]
    public static class LoadPatch
    {
        public static XmlDocument gameToLoad;

        private static bool Prefix()
        {
            if (gameToLoad == null) return true;

            SaveCompression.doSaveCompression = true;

            ScribeUtil.StartLoading(gameToLoad);
            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
            Scribe.EnterNode("game");
            Current.Game = new Game();
            Current.Game.LoadGame(); // calls Scribe.loader.FinalizeLoading()

            SaveCompression.doSaveCompression = false;
            gameToLoad = null;

            Log.Message("Game loaded");

            if (Multiplayer.Client != null)
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    // Inits all caches
                    foreach (ITickable tickable in TickPatch.AllTickables.Where(t => !(t is ConstantTicker)))
                        tickable.Tick();

                    if (!Current.Game.Maps.Any())
                    {
                        MemoryUtility.UnloadUnusedUnityAssets();
                        Find.World.renderer.RegenerateAllLayersNow();
                    }
                });

            return false;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.ExposeSmallComponents))]
    internal static class GameExposeComponentsPatch
    {
        private static void Prefix()
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Multiplayer.game = new MultiplayerGame();
        }
    }

    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    internal static class ClearAllPatch
    {
        private static void Postfix()
        {
            Multiplayer.game = null;
        }
    }

    [HarmonyPatch(typeof(FactionManager), nameof(FactionManager.RecacheFactions))]
    internal static class RecacheFactionsPatch
    {
        private static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.game.dummyFaction = Find.FactionManager.GetById(-1);
        }
    }

    [HarmonyPatch(typeof(World), nameof(World.ExposeComponents))]
    internal static class SaveWorldComp
    {
        private static void Postfix(World __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref Multiplayer.game.worldComp, "mpWorldComp", __instance);

                if (Multiplayer.game.worldComp == null)
                {
                    Log.Error("No MultiplayerWorldComp during loading/saving");
                    Multiplayer.game.worldComp = new MultiplayerWorldComp(__instance);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.ExposeComponents))]
    internal static class SaveMapComps
    {
        private static void Postfix(Map __instance)
        {
            if (Multiplayer.Client == null) return;

            MapAsyncTimeComp asyncTime = __instance.AsyncTime();
            MultiplayerMapComp comp = __instance.MpComp();

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref asyncTime, "mpAsyncTime", __instance);
                Scribe_Deep.Look(ref comp, "mpMapComp", __instance);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (asyncTime == null)
                {
                    Log.Error($"{typeof(MapAsyncTimeComp)} missing during loading");
                    // This is just so the game doesn't completely freeze
                    asyncTime = new MapAsyncTimeComp(__instance);
                }

                Multiplayer.game.asyncTimeComps.Add(asyncTime);

                if (comp == null)
                {
                    Log.Error($"{typeof(MultiplayerMapComp)} missing during loading");
                    comp = new MultiplayerMapComp(__instance);
                }

                Multiplayer.game.mapComps.Add(comp);
            }
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.MapComponentTick))]
    internal static class MapCompTick
    {
        private static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.MpComp()?.DoTick();
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit))]
    internal static class MapCompFinalizeInit
    {
        private static void Postfix(Map map)
        {
            if (Multiplayer.Client == null) return;
            map.AsyncTime()?.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(WorldComponentUtility), nameof(WorldComponentUtility.FinalizeInit))]
    internal static class WorldCompFinalizeInit
    {
        private static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            Multiplayer.WorldComp.FinalizeInit();
        }
    }

    [HarmonyPatch(typeof(Alert), nameof(Alert.Notify_Started))]
    internal static class FixAlertBellTime
    {
        private static void Postfix(Alert __instance)
        {
            if (__instance.lastBellTime == float.NaN)
                __instance.lastBellTime = Time.realtimeSinceStartup;
        }
    }
}