#region

using Harmony;
using RimWorld;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TutorSystem), nameof(TutorSystem.AdaptiveTrainingEnabled), MethodType.Getter)]
    internal static class DisableAdaptiveLearningPatch
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    internal static class AdaptiveLearning_PrefsPatch
    {
        [MpPostfix(typeof(Prefs), "get_" + nameof(Prefs.AdaptiveTrainingEnabled))]
        private static void Getter_Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }

        [MpPrefix(typeof(Prefs), "set_" + nameof(Prefs.AdaptiveTrainingEnabled))]
        private static bool Setter_Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.VolumeGame))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    internal static class CancelDuringSkipping
    {
        private static bool Prefix()
        {
            return !TickPatch.Skipping;
        }
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.MaxNumberOfPlayerSettlements), MethodType.Getter)]
    internal static class MaxColoniesPatch
    {
        private static void Postfix(ref int __result)
        {
            if (Multiplayer.Client != null)
                __result = 5;
        }
    }

    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Getter)]
    internal static class RunInBackgroundPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = true;
        }
    }

    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "get_" + nameof(Prefs.PauseOnUrgentLetter))]
    internal static class PrefGettersInMultiplayer
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }

    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnLoad))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnError))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.PauseOnUrgentLetter))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.MaxNumberOfPlayerSettlements))]
    [MpPatch(typeof(Prefs), "set_" + nameof(Prefs.RunInBackground))]
    internal static class PrefSettersInMultiplayer
    {
        private static bool Prefix()
        {
            return Multiplayer.Client == null;
        }
    }
}