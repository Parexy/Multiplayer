#region

using System;
using Harmony;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

#endregion

namespace Multiplayer.Client
{
    // Set the map time for GUI methods depending on it
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate))]
    [MpPatch(typeof(SoundRoot), nameof(SoundRoot.Update))]
    [MpPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor))]
    internal static class SetMapTimeForUI
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        private static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || WorldRendererUtility.WorldRenderedNow || Find.CurrentMap == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(Find.CurrentMap);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [MpPatch(typeof(Map), nameof(Map.MapUpdate))]
    [MpPatch(typeof(Map), nameof(Map.FinalizeLoading))]
    internal static class MapUpdateTimePatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        private static void Prefix(Map __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [MpPatch(typeof(PortraitsCache), nameof(PortraitsCache.IsAnimated))]
    internal static class PawnPortraitMapTime
    {
        private static void Prefix(Pawn pawn, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(pawn.MapHeld);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPortrait))]
    internal static class PawnRenderPortraitMapTime
    {
        private static void Prefix(PawnRenderer __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn.MapHeld);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    internal static class PreDrawPosCalculationMapTime
    {
        private static void Prefix(PawnTweener __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn.Map);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    internal static class DangerRatingMapTime
    {
        private static void Prefix(DangerWatcher __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.map);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [MpPatch(typeof(Sustainer), nameof(Sustainer.SustainerUpdate))]
    [MpPatch(typeof(Sustainer), "<Sustainer>m__0")]
    internal static class SustainerUpdateMapTime
    {
        private static void Prefix(Sustainer __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.info.Maker.Map);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(Sample), nameof(Sample.Update))]
    internal static class SampleUpdateMapTime
    {
        private static void Prefix(Sample __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.Map);
        }

        private static void Postfix(TimeSnapshot? __state)
        {
            __state?.Set();
        }
    }

    [HarmonyPatch(typeof(TipSignal), MethodType.Constructor, new[] {typeof(Func<string>), typeof(int)})]
    internal static class TipSignalCtor
    {
        private static void Prefix(ref Func<string> textGetter)
        {
            if (Multiplayer.Client == null) return;

            var current = TimeSnapshot.Current();
            var getter = textGetter;

            textGetter = () =>
            {
                var prev = TimeSnapshot.Current();
                current.Set();
                var s = getter();
                prev.Set();

                return s;
            };
        }
    }

    public struct TimeSnapshot
    {
        public int ticks;
        public TimeSpeed speed;
        public TimeSlower slower;

        public void Set()
        {
            Find.TickManager.ticksGameInt = ticks;
            Find.TickManager.slower = slower;
            Find.TickManager.curTimeSpeed = speed;
        }

        public static TimeSnapshot Current()
        {
            return new TimeSnapshot()
            {
                ticks = Find.TickManager.ticksGameInt,
                speed = Find.TickManager.curTimeSpeed,
                slower = Find.TickManager.slower
            };
        }

        public static TimeSnapshot? GetAndSetFromMap(Map map)
        {
            if (map == null) return null;

            var prev = Current();

            var man = Find.TickManager;
            var comp = map.AsyncTime();

            man.ticksGameInt = comp.mapTicks;
            man.slower = comp.slower;
            man.CurTimeSpeed = comp.TimeSpeed;

            return prev;
        }
    }
}