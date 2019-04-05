#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    // Fixes a lag spike when opening debug tools
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch(nameof(UIRoot.UIRootOnGUI))]
    internal static class UIRootPatch
    {
        private static bool done;

        private static void Prefix()
        {
            if (done) return;
            GUI.skin.font = Text.fontStyles[1].font;
            Text.fontStyles[1].font.fontNames = new string[] {"arial", "arialbd", "ariali", "arialbi"};
            done = true;
        }
    }

    // Fix window focus handling
    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.CloseWindowsBecauseClicked))]
    public static class WindowFocusPatch
    {
        private static void Prefix(WindowStack __instance, Window clickedWindow)
        {
            for (var i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                var window = Find.WindowStack.Windows[i];
                __instance.focusedWindow = window;

                if (window == clickedWindow || window.closeOnClickedOutside) return;
                UI.UnfocusCurrentControl();
            }

            __instance.focusedWindow = null;
        }
    }

    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.AllLeafSubclasses))]
    internal static class AllLeafSubclassesPatch
    {
        public static HashSet<Type> hasSubclasses;

        private static bool Prefix()
        {
            if (hasSubclasses == null)
            {
                hasSubclasses = new HashSet<Type>();
                foreach (var t in GenTypes.AllTypes)
                    if (t.BaseType != null)
                        hasSubclasses.Add(t.BaseType);
            }

            return false;
        }

        private static void Postfix(Type baseType, ref IEnumerable<Type> __result)
        {
            __result = baseType.AllSubclasses().Where(t => !hasSubclasses.Contains(t));
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_PawnsArrive), nameof(IncidentWorker_PawnsArrive.FactionCanBeGroupSource))]
    internal static class FactionCanBeGroupSourcePatch
    {
        private static void Postfix(Faction f, ref bool __result)
        {
            __result &= f.def.pawnGroupMakers?.Count > 0;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.AddNewImmediateWindow))]
    internal static class LongEventWindowPreventCameraMotion
    {
        public const int LongEventWindowId = 62893994;

        private static void Postfix(int ID)
        {
            if (ID == -LongEventWindowId || ID == -MainButtonsPatch.SkippingWindowId)
            {
                var window = Find.WindowStack.windows.Find(w => w.ID == ID);

                window.absorbInputAroundWindow = true;
                window.preventCameraMotion = true;
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.WindowOnGUI))]
    internal static class WindowDrawDarkBackground
    {
        private static void Prefix(Window __instance)
        {
            if (Current.ProgramState == ProgramState.Entry) return;

            if (__instance.ID == -LongEventWindowPreventCameraMotion.LongEventWindowId ||
                __instance.ID == -MainButtonsPatch.SkippingWindowId ||
                __instance is DisconnectedWindow ||
                __instance is MpFormingCaravanWindow
            )
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(0, 0, 0, 0.5f));
        }
    }

    // Fixes a bug with long event handler's immediate window draw order
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.ImmediateWindow))]
    internal static class AddImmediateWindowsDuringLayouting
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var found = false;

            foreach (var inst in insts)
            {
                if (!found && inst.opcode == OpCodes.Ldc_I4_7)
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(AddImmediateWindowsDuringLayouting), nameof(Process)));
                    found = true;
                }

                yield return inst;
            }
        }

        private static EventType Process(EventType type)
        {
            return type == EventType.layout ? EventType.repaint : type;
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawLine))]
    internal static class DrawLineOnlyOnRepaint
    {
        private static bool Prefix()
        {
            return Event.current.type == EventType.repaint;
        }
    }

    // Use a simpler shader for plants when possible
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.PlantWindSway), MethodType.Setter)]
    internal static class PlantWindSwayPatch
    {
        public static void Init()
        {
            Prefix(Prefs.PlantWindSway);
        }

        private static void Prefix(bool value)
        {
            foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
                if (thingDef.category == ThingCategory.Plant &&
                    thingDef.graphicData != null &&
                    thingDef.graphicData.shaderParameters == null &&
                    thingDef.graphicData.shaderType?.defName == "CutoutPlant"
                )
                    thingDef.graphic.MatSingle.shader = value ? ShaderDatabase.CutoutPlant : ShaderDatabase.Cutout;
        }
    }
}