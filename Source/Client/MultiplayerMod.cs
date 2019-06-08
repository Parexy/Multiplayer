using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Harmony;
using Multiplayer.Common;
using Multiplayer.Server;
using Multiplayer.Server.Networking.Handler;
using RimWorld;
using UnityEngine;
using Verse;

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
            foreach (var type in asm.GetTypes())
            {
                MpPatchExtensions.DoMpPatches(null, type)?.ForEach(m => MpUtil.MarkNoInlining(m));

                var harmonyMethods = type.GetHarmonyMethods();
                if (harmonyMethods?.Count > 0)
                {
                    var original = MpUtil.GetOriginalMethod(HarmonyMethod.Merge(harmonyMethods));
                    if (original != null)
                        MpUtil.MarkNoInlining(original);
                }
            }
        }

        private void EarlyPatches()
        {
            Log.Message("[MP] Running early patches...");
            // special case?
            MpUtil.MarkNoInlining(AccessTools.Method(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)));

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var firstMethod = asm.GetType("Harmony.AccessTools")?.GetMethod("FirstMethod");
                if (firstMethod != null)
                    harmony.Patch(firstMethod,
                        new HarmonyMethod(typeof(AccessTools_FirstMethod_Patch),
                            nameof(AccessTools_FirstMethod_Patch.Prefix)));

                if (asm == typeof(HarmonyPatch).Assembly) continue;

                var emitCallParameter = asm.GetType("Harmony.MethodPatcher")
                    ?.GetMethod("EmitCallParameter", AccessTools.all);
                if (emitCallParameter != null)
                    harmony.Patch(emitCallParameter,
                        new HarmonyMethod(typeof(PatchHarmony),
                            emitCallParameter.GetParameters().Length == 4
                                ? nameof(PatchHarmony.EmitCallParamsPrefix4)
                                : nameof(PatchHarmony.EmitCallParamsPrefix5)));
            }

            {
                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CaptureThingSetMakers), "Prefix"));
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_MarketValue)), prefix);
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_Nutrition)), prefix);
            }

            Log.Message("[MP] Patching get_DescendantThingDefs");

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            Log.Message("[MP] Patching get_ThisAndChildCategoryDefs");

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            Log.Message("[MP] Patching ParseAndProcessXML");

            harmony.Patch(
                AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)),
                transpiler: new HarmonyMethod(typeof(ParseAndProcessXml_Patch), "Transpiler")
            );

            Log.Message("[MP] Patching get_ChildNodes");

            harmony.Patch(
                AccessTools.Method(typeof(XmlNode), "get_ChildNodes"),
                postfix: new HarmonyMethod(typeof(XmlNodeListPatch),
                    nameof(XmlNodeListPatch.XmlNode_ChildNodes_Postfix))
            );

            Log.Message("[MP] Patching TryRegisterAllFrom");

            harmony.Patch(
                AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.TryRegisterAllFrom)),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Prefix"),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Postfix")
            );

            Log.Message("[MP] Patching LoadableXmlAcces ctor");

            harmony.Patch(
                AccessTools.Constructor(typeof(LoadableXmlAsset),
                    new[] {typeof(string), typeof(string), typeof(string)}),
                new HarmonyMethod(typeof(LoadableXmlAssetCtorPatch), "Prefix")
            );

            Log.Message("[MP] Patching Def ctor");

            // Cross os compatibility
            harmony.Patch(
                AccessTools.Method(typeof (DirectXmlLoader), nameof (DirectXmlLoader.XmlAssetsInModFolder)), null,
                new HarmonyMethod(typeof(XmlAssetsInModFolderPatch), "Postfix")
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
            var listing = new Listing_Standard();
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

            var appendNameToAutosaveLabel = $"{"MpAppendNameToAutosave".Translate()}:  ";
            var appendNameToAutosaveLabelWidth = Text.CalcSize(appendNameToAutosaveLabel).x;
            var appendNameToAutosaveCheckboxWidth = appendNameToAutosaveLabelWidth + 30f;
            listing.CheckboxLabeled(appendNameToAutosaveLabel, ref settings.appendNameToAutosave);

            listing.CheckboxLabeled("MpPauseAutosaveCounter".Translate(), ref settings.pauseAutosaveCounter,
                "MpPauseAutosaveCounterDesc".Translate());

            if (Prefs.DevMode)
                listing.CheckboxLabeled("Show debug info", ref settings.showDevInfo);

            listing.End();
        }

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName(UsernameField);

            var username = listing.TextEntryLabeled("MpUsername".Translate() + ":  ", settings.username);
            if (username.Length <= 15 && ServerHandshakePacketHandler.UsernamePattern.IsMatch(username))
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

    static class XmlAssetsInModFolderPatch
    {
        static IEnumerable<LoadableXmlAsset> Postfix (IEnumerable<LoadableXmlAsset> __result)
        // Sorts the files before processing, ensures cross os compatibility
        {
            var array = __result.ToArray ();
            Array.Sort (array, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.name, y.name));


            return array;
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
        public bool appendNameToAutosave;
        public bool autoAcceptSteam;
        public int autosaveSlots = 5;
        public bool pauseAutosaveCounter = true;
        public string serverAddress = "127.0.0.1";
        public ServerSettings serverSettings;
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
            Scribe_Values.Look(ref appendNameToAutosave, "appendNameToAutosave");
            Scribe_Values.Look(ref pauseAutosaveCounter, "pauseAutosaveCounter", true);

            Scribe_Deep.Look(ref serverSettings, "serverSettings");

            if (serverSettings == null)
                serverSettings = new ServerSettings();
        }
    }
}