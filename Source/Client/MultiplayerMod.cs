#region

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HotSwappable]
    public class MultiplayerMod : Mod
    {
        private const string UsernameField = "UsernameField";
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");
        public static MpSettings settings;

        public static bool arbiterInstance;

        private string slotsBuffer;

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            if (GenCommandLine.CommandLineArgPassed("arbiter"))
                arbiterInstance = true;

            EarlyMarkNoInline(typeof(Multiplayer).Assembly);
            EarlyPatches();

            settings = GetSettings<MpSettings>();
        }

        public static void EarlyMarkNoInline(Assembly asm)
        {
            foreach (Type type in asm.GetTypes())
            {
                MpPatchExtensions.DoMpPatches(null, type)?.ForEach(m => MpUtil.MarkNoInlining(m));

                List<HarmonyMethod> harmonyMethods = type.GetHarmonyMethods();
                if (harmonyMethods?.Count > 0)
                {
                    MethodBase original = MpUtil.GetOriginalMethod(HarmonyMethod.Merge(harmonyMethods));
                    if (original != null)
                        MpUtil.MarkNoInlining(original);
                }
            }
        }

        private void EarlyPatches()
        {
            // special case?
            MpUtil.MarkNoInlining(AccessTools.Method(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)));

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                MethodInfo firstMethod = asm.GetType("Harmony.AccessTools")?.GetMethod("FirstMethod");
                if (firstMethod != null)
                    harmony.Patch(firstMethod,
                        new HarmonyMethod(typeof(AccessTools_FirstMethod_Patch),
                            nameof(AccessTools_FirstMethod_Patch.Prefix)));

                if (asm == typeof(HarmonyPatch).Assembly) continue;

                MethodInfo emitCallParameter = asm.GetType("Harmony.MethodPatcher")
                    ?.GetMethod("EmitCallParameter", AccessTools.all);
                if (emitCallParameter != null)
                    harmony.Patch(emitCallParameter,
                        new HarmonyMethod(typeof(PatchHarmony),
                            emitCallParameter.GetParameters().Length == 4
                                ? nameof(PatchHarmony.EmitCallParamsPrefix4)
                                : nameof(PatchHarmony.EmitCallParamsPrefix5)));
            }

            {
                HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(CaptureThingSetMakers), "Prefix"));
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_MarketValue)), prefix);
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_Nutrition)), prefix);
            }

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)),
                new HarmonyMethod(typeof(GetTypeInAnyAssemblyPatch), "Prefix"),
                new HarmonyMethod(typeof(GetTypeInAnyAssemblyPatch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)),
                transpiler: new HarmonyMethod(typeof(ParseAndProcessXml_Patch), "Transpiler")
            );

            harmony.Patch(
                AccessTools.Method(typeof(XmlNode), "get_ChildNodes"),
                postfix: new HarmonyMethod(typeof(XmlNodeListPatch),
                    nameof(XmlNodeListPatch.XmlNode_ChildNodes_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.TryRegisterAllFrom)),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Prefix"),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Constructor(typeof(LoadableXmlAsset),
                    new[] {typeof(string), typeof(string), typeof(string)}),
                new HarmonyMethod(typeof(LoadableXmlAssetCtorPatch), "Prefix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(ModMetaData), "<Init>m__1"),
                new HarmonyMethod(typeof(ModPreviewImagePatch), "Prefix")
            );

            // Might fix some mod desyncs
            harmony.Patch(
                AccessTools.Constructor(typeof(Def), new Type[0]),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
            );
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 220f;

            DoUsernameField(listing);
            listing.TextFieldNumericLabeled("MpAutosaveSlots".Translate() + ":  ", ref settings.autosaveSlots,
                ref slotsBuffer, 1f, 99f);

            listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref settings.showCursors);
            listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref settings.autoAcceptSteam,
                "MpAutoAcceptSteamDesc".Translate());
            listing.CheckboxLabeled("MpTransparentChat".Translate(), ref settings.transparentChat);
            listing.CheckboxLabeled("MpAggressiveTicking".Translate(), ref settings.aggressiveTicking,
                "MpAggressiveTickingDesc".Translate());

            if (Prefs.DevMode)
                listing.CheckboxLabeled("Show debug info", ref settings.showDevInfo);

            listing.End();
        }

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName(UsernameField);

            string username = listing.TextEntryLabeled("MpUsername".Translate() + ":  ", settings.username);
            if (username.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(username))
            {
                settings.username = username;
                Multiplayer.username = username;
            }

            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == UsernameField)
                UI.UnfocusCurrentControl();
        }

        public override string SettingsCategory()
        {
            return "Multiplayer";
        }
    }

    internal static class LoadableXmlAssetCtorPatch
    {
        public static List<Pair<LoadableXmlAsset, int>> xmlAssetHashes = new List<Pair<LoadableXmlAsset, int>>();

        private static void Prefix(LoadableXmlAsset __instance, string contents)
        {
            xmlAssetHashes.Add(new Pair<LoadableXmlAsset, int>(__instance, GenText.StableStringHash(contents)));
        }
    }

    internal static class ModPreviewImagePatch
    {
        private static bool Prefix()
        {
            return !MpVersion.IsDebug && !MultiplayerMod.arbiterInstance;
        }
    }

    internal static class PatchHarmony
    {
        private static readonly MethodInfo mpEmitCallParam =
            AccessTools.Method(typeof(MethodPatcher), "EmitCallParameter");

        public static bool EmitCallParamsPrefix4(ILGenerator il, MethodBase original, MethodInfo patch,
            Dictionary<string, LocalBuilder> variables)
        {
            mpEmitCallParam.Invoke(null, new object[] {il, original, patch, variables, false});
            return false;
        }

        public static bool EmitCallParamsPrefix5(ILGenerator il, MethodBase original, MethodInfo patch,
            Dictionary<string, LocalBuilder> variables, bool allowFirsParamPassthrough)
        {
            mpEmitCallParam.Invoke(null, new object[] {il, original, patch, variables, allowFirsParamPassthrough});
            return false;
        }
    }

    public class MpSettings : ModSettings
    {
        public bool aggressiveTicking;
        public bool autoAcceptSteam;
        public int autosaveSlots = 5;
        public string serverAddress;
        public bool showCursors = true;
        public bool showDevInfo;
        public bool transparentChat;
        public string username;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
            Scribe_Values.Look(ref transparentChat, "transparentChat");
            Scribe_Values.Look(ref autosaveSlots, "autosaveSlots", 5);
            Scribe_Values.Look(ref aggressiveTicking, "aggressiveTicking");
            Scribe_Values.Look(ref showDevInfo, "showDevInfo");
            Scribe_Values.Look(ref serverAddress, "serverAddress", "127.0.0.1");
        }
    }
}