#region

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [MpPatch(typeof(GUI), "get_" + nameof(GUI.skin))]
    internal static class GUISkinArbiter_Patch
    {
        private static bool Prefix(ref GUISkin __result)
        {
            if (!MultiplayerMod.arbiterInstance) return true;
            __result = ScriptableObject.CreateInstance<GUISkin>();
            return false;
        }
    }

    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(PortraitsCache), nameof(PortraitsCache.Get))]
    internal static class RenderTextureCreatePatch
    {
        private static readonly MethodInfo IsCreated = AccessTools.Method(typeof(RenderTexture), "IsCreated");

        private static readonly FieldInfo ArbiterField =
            AccessTools.Field(typeof(MultiplayerMod), nameof(MultiplayerMod.arbiterInstance));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == IsCreated)
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, ArbiterField);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [MpPatch(typeof(WaterInfo), nameof(WaterInfo.SetTextures))]
    [MpPatch(typeof(SubcameraDriver), nameof(SubcameraDriver.UpdatePositions))]
    [MpPatch(typeof(Prefs), nameof(Prefs.Save))]
    [MpPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateAllLayers))]
    [MpPatch(typeof(Section), nameof(Section.RegenerateLayers))]
    [MpPatch(typeof(SectionLayer), nameof(SectionLayer.DrawLayer))]
    [MpPatch(typeof(Map), nameof(Map.MapUpdate))]
    [MpPatch(typeof(GUIStyle), nameof(GUIStyle.CalcSize))]
    internal static class CancelForArbiter
    {
        private static bool Prefix()
        {
            return !MultiplayerMod.arbiterInstance;
        }
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    internal static class CancelWorldRendererCtor
    {
        private static bool Prefix()
        {
            return !MultiplayerMod.arbiterInstance;
        }

        private static void Postfix(WorldRenderer __instance)
        {
            if (MultiplayerMod.arbiterInstance)
                __instance.layers = new List<WorldLayer>();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    internal static class CloseLetters
    {
        private static void Postfix(LetterStack __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!TickPatch.Skipping && !MultiplayerMod.arbiterInstance) return;

            for (var i = __instance.letters.Count - 1; i >= 0; i--)
            {
                var letter = __instance.letters[i];
                if (letter is ChoiceLetter choice &&
                    choice.Choices.Any(c => c.action?.Method == choice.Option_Close.action.Method) &&
                    Time.time - letter.arrivalTime > 4)
                    __instance.RemoveLetter(letter);
            }
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    internal static class ArbiterLongEventPatch
    {
        private static void Postfix()
        {
            if (MultiplayerMod.arbiterInstance && LongEventHandler.currentEvent != null)
                LongEventHandler.currentEvent.alreadyDisplayed = true;
        }
    }
}