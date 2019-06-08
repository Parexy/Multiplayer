using System.Collections.Generic;
using System.Linq;
using Harmony;
using Multiplayer.Client.Synchronization;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public class TransporterLoading : IExposable, ISessionWithTransferables
    {
        public Map map;
        public List<ThingWithComps> pods;

        public int sessionId;
        public List<TransferableOneWay> transferables;

        public List<CompTransporter> transporters;

        public bool uiDirty;

        public TransporterLoading(Map map)
        {
            this.map = map;
        }

        public TransporterLoading(Map map, List<CompTransporter> transporters) : this(map)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.transporters = transporters;
            pods = transporters.Select(t => t.parent).ToList();

            AddItems();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
            Scribe_Collections.Look(ref pods, "transporters", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                transporters = pods.Select(t => t.GetComp<CompTransporter>()).ToList();
        }

        public int SessionId => sessionId;

        public Transferable GetTransferableByThingId(int thingId)
        {
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        public void Notify_CountChanged(Transferable tr)
        {
            uiDirty = true;
        }

        private void AddItems()
        {
            var dialog = new MpLoadTransportersWindow(map, transporters);
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;
        }

        [SyncMethod]
        public void TryAccept()
        {
            if (PrepareDummyDialog().TryAccept())
                Remove();
        }

        [SyncMethod]
        [SyncDebugOnly]
        public void DebugTryLoadInstantly()
        {
            if (PrepareDummyDialog().DebugTryLoadInstantly())
                Remove();
        }

        [SyncMethod]
        public void Reset()
        {
            transferables.ForEach(t => t.CountToTransfer = 0);
            uiDirty = true;
        }

        [SyncMethod]
        public void Remove()
        {
            map.MpComp().transporterLoading = null;
        }

        public void OpenWindow(bool sound = true)
        {
            Find.Selector.ClearSelection();

            var dialog = PrepareDummyDialog();
            if (!sound)
                dialog.soundAppear = null;
            dialog.doCloseX = true;

            dialog.CalculateAndRecacheTransferables();

            Find.WindowStack.Add(dialog);
        }

        private MpLoadTransportersWindow PrepareDummyDialog()
        {
            return new MpLoadTransportersWindow(map, transporters) {itemsReady = true, transferables = transferables};
        }
    }

    public class MpLoadTransportersWindow : Dialog_LoadTransporters
    {
        public static MpLoadTransportersWindow drawing;

        public bool itemsReady;

        public MpLoadTransportersWindow(Map map, List<CompTransporter> transporters) : base(map, transporters)
        {
        }

        public TransporterLoading Session => map.MpComp().transporterLoading;

        public override void PostClose()
        {
            base.PostClose();

            if (Session != null)
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
        }

        public override void DoWindowContents(Rect inRect)
        {
            drawing = this;

            try
            {
                var session = Session;

                if (session == null)
                {
                    Close();
                }
                else if (session.uiDirty)
                {
                    CountToTransferChanged();
                    session.uiDirty = false;
                }

                base.DoWindowContents(inRect);
            }
            finally
            {
                drawing = null;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool))]
    internal static class MakeCancelLoadingButtonRed
    {
        private static void Prefix(string label, ref bool __state)
        {
            if (MpLoadTransportersWindow.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        private static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result)
            {
                MpLoadTransportersWindow.drawing.Session?.Remove();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool))]
    internal static class LoadPodsHandleReset
    {
        private static void Prefix(string label, ref bool __state)
        {
            if (MpLoadTransportersWindow.drawing == null) return;
            if (label != "ResetButton".Translate()) return;

            __state = true;
        }

        private static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            if (__result)
            {
                MpLoadTransportersWindow.drawing.Session?.Reset();
                __result = false;
            }
        }
    }

    [MpPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.AddPawnsToTransferables))]
    [MpPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.AddItemsToTransferables))]
    internal static class CancelAddItems
    {
        private static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (__instance is MpLoadTransportersWindow mp && mp.itemsReady)
            {
                mp.transferables = mp.Session.transferables;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.TryAccept))]
    internal static class TryAcceptPodsPatch
    {
        private static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (Multiplayer.ShouldSync && __instance is MpLoadTransportersWindow dialog)
            {
                dialog.Session?.TryAccept();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), nameof(Dialog_LoadTransporters.DebugTryLoadInstantly))]
    internal static class DebugTryLoadInstantlyPatch
    {
        private static bool Prefix(Dialog_LoadTransporters __instance)
        {
            if (Multiplayer.ShouldSync && __instance is MpLoadTransportersWindow dialog)
            {
                dialog.Session?.DebugTryLoadInstantly();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class CancelDialogLoadTransporters
    {
        private static bool Prefix(Window window)
        {
            if (Multiplayer.MapContext != null && window.GetType() == typeof(Dialog_LoadTransporters))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(Map), typeof(List<CompTransporter>)})]
    internal static class CancelDialogLoadTransportersCtor
    {
        private static bool Prefix(Dialog_LoadTransporters __instance, Map map, List<CompTransporter> transporters)
        {
            if (__instance.GetType() != typeof(Dialog_LoadTransporters))
                return true;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                var comp = map.MpComp();
                if (comp.transporterLoading == null)
                    comp.CreateTransporterLoadingSession(transporters);

                return true;
            }

            return true;
        }
    }

    [MpPatch(typeof(ITab_TransporterContents), "<DoItemsLists>c__AnonStorey0", "<>m__0")]
    internal static class TransporterContents_DiscardToLoad
    {
        private static bool Prefix(int x)
        {
            if (Multiplayer.Client == null) return true;
            Messages.Message("MpNotAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}