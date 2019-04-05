#region

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using Verse;

#endregion

namespace Multiplayer.Client
{
    // Allow different factions' blueprints in the same cell
    // Ignore other factions' blueprints when building
    // Remove all blueprints when something solid is built over them
    // Don't draw other factions' blueprints
    // Don't link graphics of different factions' blueprints

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
    internal static class CanPlaceBlueprintAtPatch
    {
        private static readonly MethodInfo CanPlaceBlueprintOver =
            AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintOver));

        public static MethodInfo ShouldIgnore1Method = AccessTools.Method(typeof(CanPlaceBlueprintAtPatch),
            nameof(ShouldIgnore), new[] {typeof(Thing)});

        public static MethodInfo ShouldIgnore2Method = AccessTools.Method(typeof(CanPlaceBlueprintAtPatch),
            nameof(ShouldIgnore), new[] {typeof(ThingDef), typeof(Thing)});

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == CanPlaceBlueprintOver)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 22);
                    yield return new CodeInstruction(OpCodes.Call, ShouldIgnore1Method);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }

        private static bool ShouldIgnore(ThingDef newThing, Thing oldThing)
        {
            return newThing.IsBlueprint && ShouldIgnore(oldThing);
        }

        private static bool ShouldIgnore(Thing oldThing)
        {
            return oldThing.def.IsBlueprint && oldThing.Faction != Faction.OfPlayer;
        }
    }

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt))]
    internal static class CanPlaceBlueprintAtPatch2
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            var insts = (List<CodeInstruction>) e;

            int loop1 = new CodeFinder(original, insts).Forward(OpCodes.Ldstr, "IdenticalThingExists")
                .Backward(OpCodes.Ldarg_S, (byte) 5);

            insts.Insert(
                loop1 - 1,
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop1 + 2].operand)
            );

            int loop2 = new CodeFinder(original, insts).Forward(OpCodes.Ldstr, "InteractionSpotBlocked")
                .Backward(OpCodes.Ldarg_S, (byte) 5);

            insts.Insert(
                loop2 - 3,
                new CodeInstruction(OpCodes.Ldloc_S, 8),
                new CodeInstruction(OpCodes.Ldloc_S, 9),
                new CodeInstruction(OpCodes.Callvirt, SpawnBuildingAsPossiblePatch.ThingListGet),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop2 + 2].operand)
            );

            int loop3 = new CodeFinder(original, insts).Forward(OpCodes.Ldstr, "WouldBlockInteractionSpot")
                .Backward(OpCodes.Ldarg_S, (byte) 5);

            insts.Insert(
                loop3 - 1,
                new CodeInstruction(OpCodes.Ldloc_S, 14),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, insts[loop3 + 2].operand)
            );

            return insts;
        }
    }

    [HarmonyPatch(typeof(PlaceWorker_NeverAdjacentTrap), nameof(PlaceWorker_NeverAdjacentTrap.AllowsPlacing))]
    internal static class PlaceWorkerTrapPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e,
            MethodBase original)
        {
            var insts = (List<CodeInstruction>) e;
            var label = gen.DefineLabel();

            var finder = new CodeFinder(original, insts);
            int pos = finder.Forward(OpCodes.Stloc_S, 5);

            insts.Insert(
                pos + 1,
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, label)
            );

            int ret = finder.Start().Forward(OpCodes.Ret);
            insts[ret + 1].labels.Add(label);

            return insts;
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings))]
    internal static class WipeExistingThingsPatch
    {
        private static readonly MethodInfo SpawningWipes =
            AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.WipeAndRefundExistingThings))]
    internal static class WipeAndRefundExistingThingsPatch
    {
        private static readonly MethodInfo SpawningWipes =
            AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldloc_3);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible))]
    internal static class SpawnBuildingAsPossiblePatch
    {
        private static readonly MethodInfo SpawningWipes =
            AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes));

        public static MethodInfo ThingListGet = AccessTools.Method(typeof(List<Thing>), "get_Item");
        private static readonly FieldInfo ThingDefField = AccessTools.Field(typeof(Thing), "def");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Call && inst.operand == SpawningWipes)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ThingDefField);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 5);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 6);
                    yield return new CodeInstruction(OpCodes.Callvirt, ThingListGet);
                    yield return new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore2Method);
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GenPlace), nameof(GenPlace.HaulPlaceBlockerIn))]
    internal static class HaulPlaceBlockerInPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> e,
            MethodBase original)
        {
            var insts = (List<CodeInstruction>) e;
            var label = gen.DefineLabel();

            var finder = new CodeFinder(original, insts);
            int pos = finder.Forward(OpCodes.Stloc_2);

            insts.Insert(
                pos + 1,
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Call, CanPlaceBlueprintAtPatch.ShouldIgnore1Method),
                new CodeInstruction(OpCodes.Brtrue, label)
            );

            int ret = finder.End().Advance(-1).Backward(OpCodes.Ret);
            insts[ret + 1].labels.Add(label);

            return insts;
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    internal static class SpawningWipesBlueprintPatch
    {
        private static void Postfix(ref bool __result, BuildableDef newEntDef, BuildableDef oldEntDef)
        {
            var newDef = newEntDef as ThingDef;
            var oldDef = oldEntDef as ThingDef;
            if (newDef == null || oldDef == null) return;

            if (!newDef.IsBlueprint && oldDef.IsBlueprint &&
                !GenConstruct.CanPlaceBlueprintOver(GenConstruct.BuiltDefOf(oldDef), newDef))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Print))]
    internal static class BlueprintPrintPatch
    {
        private static bool Prefix(Thing __instance)
        {
            if (Multiplayer.Client == null || !__instance.def.IsBlueprint) return true;
            return __instance.Faction == null || __instance.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    // LinkGrid is one building per cell, so only the player faction's blueprints are shown and linked
    [HarmonyPatch(typeof(LinkGrid), nameof(LinkGrid.Notify_LinkerCreatedOrDestroyed))]
    internal static class LinkGridBlueprintPatch
    {
        private static bool Prefix(Thing linker)
        {
            return !linker.def.IsBlueprint || linker.Faction == Multiplayer.RealPlayerFaction;
        }
    }

    // todo revisit for pvp
    //[HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.DesignateSingleCell))]
    internal static class DisableInstaBuild
    {
        private static readonly MethodInfo GetStatValueAbstract =
            AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValueAbstract));

        private static readonly MethodInfo WorkToBuildMethod =
            AccessTools.Method(typeof(DisableInstaBuild), nameof(WorkToBuild));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e, MethodBase original)
        {
            var insts = (List<CodeInstruction>) e;
            int pos = new CodeFinder(original, insts).Forward(OpCodes.Call, GetStatValueAbstract);
            insts[pos + 1] = new CodeInstruction(OpCodes.Call, WorkToBuildMethod);

            return insts;
        }

        private static float WorkToBuild()
        {
            return Multiplayer.Client == null ? 0f : -1f;
        }
    }

    [HarmonyPatch(typeof(Frame))]
    [HarmonyPatch(nameof(Frame.WorkToBuild), MethodType.Getter)]
    internal static class NoZeroWorkFrames
    {
        private static void Postfix(ref float __result)
        {
            __result = Math.Max(5, __result); // >=5 otherwise the game complains about jobs starting too fast
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints),
        nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.NoCostFrameMakeJobFor))]
    internal static class OnlyConstructorsPlaceNoCostFrames
    {
        private static readonly MethodInfo IsConstructionMethod =
            AccessTools.Method(typeof(OnlyConstructorsPlaceNoCostFrames), nameof(IsConstruction));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.opcode == OpCodes.Isinst && inst.operand == typeof(Blueprint))
                {
                    yield return new CodeInstruction(OpCodes.Ldnull);
                    yield return new CodeInstruction(OpCodes.Cgt_Un);

                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, IsConstructionMethod);

                    yield return new CodeInstruction(OpCodes.And);
                }
            }
        }

        private static bool IsConstruction(WorkGiver w)
        {
            return w.def.workType == WorkTypeDefOf.Construction;
        }
    }
}