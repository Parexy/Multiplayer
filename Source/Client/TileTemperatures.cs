#region

using Harmony;
using RimWorld.Planet;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    internal static class CachedTileTemperatureData_CheckCache
    {
        private static void Prefix(int ___tile, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;

            Map map = Current.Game.FindMap(___tile);
            if (map == null) return;

            __state = TimeSnapshot.GetAndSetFromMap(map);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.RetrieveCachedData))]
    internal static class RetrieveCachedData_Patch
    {
        private static bool Prefix(TileTemperaturesComp __instance, int tile,
            ref TileTemperaturesComp.CachedTileTemperatureData __result)
        {
            if (Multiplayer.InInterface && __instance != Multiplayer.WorldComp.uiTemperatures)
            {
                __result = Multiplayer.WorldComp.uiTemperatures.RetrieveCachedData(tile);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.WorldComponentTick))]
    internal static class TileTemperaturesTick_Patch
    {
        private static void Prefix(TileTemperaturesComp __instance)
        {
            if (Multiplayer.InInterface && __instance != Multiplayer.WorldComp.uiTemperatures)
                Multiplayer.WorldComp.uiTemperatures.WorldComponentTick();
        }
    }
}