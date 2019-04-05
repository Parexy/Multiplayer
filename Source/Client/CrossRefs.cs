#region

using Harmony;
using RimWorld;
using RimWorld.Planet;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.SpawnSetup))]
    public static class ThingSpawnPatch
    {
        private static void Postfix(Thing __instance)
        {
            if (Multiplayer.game == null) return;

            if (__instance.def.HasThingIDNumber)
            {
                ScribeUtil.sharedCrossRefs.RegisterLoaded(__instance);
                ThingsById.Register(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch(nameof(Thing.DeSpawn))]
    public static class ThingDeSpawnPatch
    {
        private static void Postfix(Thing __instance)
        {
            if (Multiplayer.game == null) return;

            ScribeUtil.sharedCrossRefs.Unregister(__instance);
            ThingsById.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.AddShip))]
    public static class ShipManagerAddPatch
    {
        private static void Postfix(PassingShip vis)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(vis);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.RemoveShip))]
    public static class ShipManagerRemovePatch
    {
        private static void Postfix(PassingShip vis)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(vis);
        }
    }

    [HarmonyPatch(typeof(PassingShipManager))]
    [HarmonyPatch(nameof(PassingShipManager.ExposeData))]
    public static class ShipManagerExposePatch
    {
        private static void Postfix(PassingShipManager __instance)
        {
            if (Multiplayer.game == null) return;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var ship in __instance.passingShips)
                    ScribeUtil.sharedCrossRefs.RegisterLoaded(ship);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.AddBill))]
    public static class BillStackAddPatch
    {
        private static void Postfix(Bill bill)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.RemoveIncompletableBills))]
    public static class BillStackRemoveIncompletablePatch
    {
        private static void Prefix(BillStack __instance)
        {
            if (Multiplayer.game == null) return;

            foreach (var bill in __instance.bills)
                if (!bill.CompletableEver)
                    ScribeUtil.sharedCrossRefs.Unregister(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.Delete))]
    public static class BillStackDeletePatch
    {
        private static void Postfix(Bill bill)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(bill);
        }
    }

    [HarmonyPatch(typeof(BillStack))]
    [HarmonyPatch(nameof(BillStack.ExposeData))]
    public static class BillStackExposePatch
    {
        private static void Postfix(BillStack __instance)
        {
            if (Multiplayer.game == null) return;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var bill in __instance.bills)
                    ScribeUtil.sharedCrossRefs.RegisterLoaded(bill);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.SpawnSetup))]
    public static class WorldObjectSpawnPatch
    {
        private static void Postfix(WorldObject __instance)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.PostRemove))]
    public static class WorldObjectRemovePatch
    {
        private static void Postfix(WorldObject __instance)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(FactionManager))]
    [HarmonyPatch(nameof(FactionManager.Add))]
    public static class FactionAddPatch
    {
        private static void Postfix(Faction faction)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(faction);
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.AddMap))]
    public static class AddMapPatch
    {
        private static void Postfix(Map map)
        {
            if (Multiplayer.game == null) return;
            ScribeUtil.sharedCrossRefs.RegisterLoaded(map);
        }
    }

    [HarmonyPatch(typeof(MapDeiniter))]
    [HarmonyPatch(nameof(MapDeiniter.Deinit))]
    public static class DeinitMapPatch
    {
        private static void Prefix(Map map)
        {
            if (Multiplayer.game == null) return;

            ScribeUtil.sharedCrossRefs.UnregisterAllFrom(map);
            ThingsById.UnregisterAllFrom(map);

            ScribeUtil.sharedCrossRefs.Unregister(map);
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
    public static class FinalizeLoadingGame
    {
        private static void Postfix()
        {
            if (Multiplayer.game == null) return;
            if (!LoadGameMarker.loading) return;

            RegisterCrossRefs();
        }

        private static void RegisterCrossRefs()
        {
            ScribeUtil.sharedCrossRefs.RegisterLoaded(Find.World);

            foreach (var f in Find.FactionManager.AllFactions)
                ScribeUtil.sharedCrossRefs.RegisterLoaded(f);

            foreach (var map in Find.Maps)
                ScribeUtil.sharedCrossRefs.RegisterLoaded(map);
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.RegisterLoaded))]
    public static class LoadedObjectsRegisterPatch
    {
        private static bool Prefix(LoadedObjectDirectory __instance, ILoadReferenceable reffable)
        {
            if (!(__instance is SharedCrossRefs)) return true;
            if (reffable == null) return false;

            var key = reffable.GetUniqueLoadID();
            if (ScribeUtil.sharedCrossRefs.allObjectsByLoadID.ContainsKey(key)) return false;

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                ScribeUtil.sharedCrossRefs.tempKeys.Add(key);

            ScribeUtil.sharedCrossRefs.allObjectsByLoadID.Add(key, reffable);

            return false;
        }
    }

    [HarmonyPatch(typeof(LoadedObjectDirectory))]
    [HarmonyPatch(nameof(LoadedObjectDirectory.Clear))]
    public static class LoadedObjectsClearPatch
    {
        private static bool Prefix(LoadedObjectDirectory __instance)
        {
            if (!(__instance is SharedCrossRefs)) return true;

            Scribe.loader.crossRefs.loadedObjectDirectory = ScribeUtil.defaultCrossRefs;
            ScribeUtil.sharedCrossRefs.UnregisterAllTemp();

            return false;
        }
    }
}