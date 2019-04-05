﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using Harmony;
using RimWorld;
using Verse;
using Verse.Profile;

#endregion

namespace Multiplayer.Client
{
    public static class SaveCompression
    {
        private const string CompressedRocks = "compressedRocks";
        private const string CompressedPlants = "compressedPlants";
        private const string CompressedRockRubble = "compressedRockRubble";
        public static bool doSaveCompression;
        private static Dictionary<ushort, ThingDef> thingDefsByShortHash;

        private static readonly HashSet<string> savePlants = new HashSet<string>()
        {
            "Plant_Grass",
            "Plant_TallGrass",
            "Plant_TreeOak",
            "Plant_TreePoplar",
            "Plant_TreeBirch",
            "Plant_TreePine",
            "Plant_Bush",
            "Plant_Brambles",
            "Plant_Dandelion",
            "Plant_Berry",
            "Plant_Moss",
            "Plant_SaguaroCactus",
            "Plant_ShrubLow",
            "Plant_TreeWillow",
            "Plant_TreeCypress",
            "Plant_TreeMaple",
            "Plant_Chokevine",
            "Plant_HealrootWild"
        };

        public static void Save(Map map)
        {
            var decider = new CompressibilityDecider(map);
            map.compressor.compressibilityDecider = decider;
            decider.DetermineReferences();

            var rockData = new BinaryWriter(new MemoryStream());
            var rockRubbleData = new BinaryWriter(new MemoryStream());
            var plantData = new BinaryWriter(new MemoryStream());

            var cells = map.info.NumCells;
            for (var i = 0; i < cells; i++)
            {
                var cell = map.cellIndices.IndexToCell(i);
                SaveRock(map, rockData, cell);
                SaveRockRubble(map, rockRubbleData, cell);
                SavePlant(map, plantData, cell);
            }

            SaveBinary(rockData, CompressedRocks);
            SaveBinary(rockRubbleData, CompressedRockRubble);
            SaveBinary(plantData, CompressedPlants);
        }

        private static void SaveRock(Map map, BinaryWriter writer, IntVec3 cell)
        {
            var thing = map.thingGrid.ThingsListAt(cell).Find(IsSaveRock);

            if (thing != null && thing.IsSaveCompressible())
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
            }
            else
            {
                writer.Write((ushort) 0);
            }
        }

        private static void SaveRockRubble(Map map, BinaryWriter writer, IntVec3 cell)
        {
            var thing = (Filth) map.thingGrid.ThingsListAt(cell).Find(IsSaveRockRubble);

            if (thing != null && thing.IsSaveCompressible())
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
                writer.Write((byte) thing.thickness);
                writer.Write(thing.growTick);
            }
            else
            {
                writer.Write((ushort) 0);
            }
        }

        private static void SavePlant(Map map, BinaryWriter writer, IntVec3 cell)
        {
            var thing = (Plant) map.thingGrid.ThingsListAt(cell).Find(IsSavePlant);

            if (thing != null && thing.IsSaveCompressible())
            {
                writer.Write(thing.def.shortHash);
                writer.Write(thing.thingIDNumber);
                writer.Write(thing.HitPoints);

                var growth = (byte) Math.Ceiling(thing.Growth * 255);
                writer.Write(growth);
                writer.Write(thing.Age);

                var hasUnlit = thing.unlitTicks != 0;
                var hasLeaflessTick = thing.madeLeaflessTick != -99999;

                var field = (byte) (thing.sown ? 1 : 0);
                field |= (byte) (hasUnlit ? 2 : 0);
                field |= (byte) (hasLeaflessTick ? 4 : 0);
                writer.Write(field);

                if (hasUnlit)
                    writer.Write(thing.unlitTicks);

                if (hasLeaflessTick)
                    writer.Write(thing.madeLeaflessTick);
            }
            else
            {
                writer.Write((ushort) 0);
            }
        }

        public static void Load(Map map)
        {
            thingDefsByShortHash = new Dictionary<ushort, ThingDef>();

            foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
                thingDefsByShortHash[thingDef.shortHash] = thingDef;

            var rockData = LoadBinary(CompressedRocks);
            var rockRubbleData = LoadBinary(CompressedRockRubble);
            var plantData = LoadBinary(CompressedPlants);
            var loadedThings = new List<Thing>();

            var cells = map.info.NumCells;
            for (var i = 0; i < cells; i++)
            {
                var cell = map.cellIndices.IndexToCell(i);
                Thing t;

                if (rockData != null && (t = LoadRock(map, rockData, cell)) != null) loadedThings.Add(t);
                if (rockRubbleData != null && (t = LoadPlant(map, plantData, cell)) != null) loadedThings.Add(t);
                if (plantData != null && (t = LoadRockRubble(map, rockRubbleData, cell)) != null) loadedThings.Add(t);
            }

            for (var i = 0; i < loadedThings.Count; i++)
            {
                var thing = loadedThings[i];
                Scribe.loader.crossRefs.loadedObjectDirectory.RegisterLoaded(thing);
            }

            DecompressedThingsPatch.thingsToSpawn[map.uniqueID] = loadedThings;
        }

        private static Thing LoadRock(Map map, BinaryReader reader, IntVec3 cell)
        {
            var defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            var id = reader.ReadInt32();
            var def = thingDefsByShortHash[defId];

            var thing = (Thing) Activator.CreateInstance(def.thingClass);
            thing.def = def;
            thing.HitPoints = thing.MaxHitPoints;

            thing.thingIDNumber = id;
            thing.SetPositionDirect(cell);

            return thing;
        }

        private static Thing LoadRockRubble(Map map, BinaryReader reader, IntVec3 cell)
        {
            var defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            var id = reader.ReadInt32();
            var thickness = reader.ReadByte();
            var growTick = reader.ReadInt32();
            var def = thingDefsByShortHash[defId];

            var thing = (Filth) Activator.CreateInstance(def.thingClass);
            thing.def = def;

            thing.thingIDNumber = id;
            thing.thickness = thickness;
            thing.growTick = growTick;

            thing.SetPositionDirect(cell);
            return thing;
        }

        private static Thing LoadPlant(Map map, BinaryReader reader, IntVec3 cell)
        {
            var defId = reader.ReadUInt16();
            if (defId == 0)
                return null;

            var id = reader.ReadInt32();
            var hitPoints = reader.ReadInt32();

            var growthByte = reader.ReadByte();
            var growth = growthByte / 255f;

            var age = reader.ReadInt32();

            var field = reader.ReadByte();
            var sown = (field & 1) != 0;
            var hasUnlit = (field & 2) != 0;
            var hasLeafless = (field & 4) != 0;

            var plantUnlitTicks = 0;
            var plantMadeLeaflessTick = -99999;

            if (hasUnlit)
                plantUnlitTicks = reader.ReadInt32();
            if (hasLeafless)
                plantMadeLeaflessTick = reader.ReadInt32();

            var def = thingDefsByShortHash[defId];

            var thing = (Plant) Activator.CreateInstance(def.thingClass);
            thing.def = def;
            thing.thingIDNumber = id;
            thing.HitPoints = hitPoints;

            thing.InitializeComps();

            thing.Growth = growth;
            thing.Age = age;
            thing.unlitTicks = plantUnlitTicks;
            thing.madeLeaflessTick = plantMadeLeaflessTick;
            thing.sown = sown;

            thing.SetPositionDirect(cell);
            return thing;
        }

        public static bool IsSaveRock(Thing t)
        {
            return t.def.saveCompressible && (!t.def.useHitPoints || t.HitPoints == t.MaxHitPoints);
        }

        public static bool IsSavePlant(Thing t)
        {
            return savePlants.Contains(t.def.defName);
        }

        public static bool IsSaveRockRubble(Thing t)
        {
            return t.def == ThingDefOf.Filth_RubbleRock;
        }

        private static void SaveBinary(BinaryWriter writer, string label)
        {
            var arr = (writer.BaseStream as MemoryStream).ToArray();
            DataExposeUtility.ByteArray(ref arr, label);
        }

        private static BinaryReader LoadBinary(string label)
        {
            byte[] arr = null;
            DataExposeUtility.ByteArray(ref arr, label);
            if (arr == null) return null;

            return new BinaryReader(new MemoryStream(arr));
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.BuildCompressedString))]
    public static class SaveCompressPatch
    {
        private static bool Prefix(MapFileCompressor __instance)
        {
            return !SaveCompression.doSaveCompression;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor))]
    [HarmonyPatch(nameof(MapFileCompressor.ExposeData))]
    public static class SaveDecompressPatch
    {
        private static bool Prefix(MapFileCompressor __instance)
        {
            if (!SaveCompression.doSaveCompression) return true;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                SaveCompression.Load(__instance.map);
            else if (Scribe.mode == LoadSaveMode.Saving)
                SaveCompression.Save(__instance.map);

            return false;
        }
    }

    [HarmonyPatch(typeof(MapFileCompressor), nameof(MapFileCompressor.ThingsToSpawnAfterLoad))]
    public static class DecompressedThingsPatch
    {
        public static Dictionary<int, List<Thing>> thingsToSpawn = new Dictionary<int, List<Thing>>();

        private static void Postfix(MapFileCompressor __instance, ref IEnumerable<Thing> __result)
        {
            if (!SaveCompression.doSaveCompression) return;
            __result = thingsToSpawn[__instance.map.uniqueID];
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeLoading))]
    internal static class ClearThingsToSpawn
    {
        private static void Postfix(Map __instance)
        {
            DecompressedThingsPatch.thingsToSpawn.Remove(__instance.uniqueID);
        }
    }

    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    internal static class ClearAllThingsToSpawn
    {
        private static void Postfix()
        {
            DecompressedThingsPatch.thingsToSpawn.Clear();
        }
    }

    [HarmonyPatch(typeof(CompressibilityDeciderUtility))]
    [HarmonyPatch(nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
    public static class SaveCompressiblePatch
    {
        private static bool Prefix()
        {
            return !SaveCompression.doSaveCompression;
        }

        private static void Postfix(Thing t, ref bool __result)
        {
            if (!SaveCompression.doSaveCompression)
            {
                if (Multiplayer.Client != null)
                    __result = false;

                return;
            }

            if (!t.Spawned) return;

            var mpCompressible = SaveCompression.IsSavePlant(t) || SaveCompression.IsSaveRock(t) ||
                                 SaveCompression.IsSaveRockRubble(t);
            __result = mpCompressible && !Referenced(t);
        }

        private static bool Referenced(Thing t)
        {
            return t.Map?.compressor?.compressibilityDecider.IsReferenced(t) ?? false;
        }
    }
}