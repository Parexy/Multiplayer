using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    class CancelDialogSplitCaravan
    {

        static bool Prefix(Window window)
        {
            if (Multiplayer.Client == null) return true;

            if (window is Dialog_SplitCaravan)
            {
                return false;
            }
            return true;
        }
    }


    [HarmonyPatch(typeof(Dialog_SplitCaravan), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Caravan) })]
    class CancelDialogSplitCaravanCtor
    {
        public static bool cancel;

        static bool Prefix(Caravan caravan)
        {
            if (Multiplayer.Client == null) return true;

            if (cancel) return false;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
                return false;

            //start caravan spltting session here by calling new session constructor

            if (Multiplayer.WorldComp.splitSession == null)
                Multiplayer.WorldComp.splitSession = new MpCaravanSplitSession(caravan);

            if (TickPatch.currentExecutingCmdIssuedBySelf)
                Find.WindowStack.Add(new MpCaravanSplitWindow(caravan));

            return true;
        }
    }

    public class MpCaravanSplitSession : IExposable, ISessionWithTransferables
    {
        private int sessionId;
        public int SessionId { get { return sessionId; } }
        public List<TransferableOneWay> transferables;
        public bool uiDirty;
        public Caravan caravan;

        public MpCaravanSplitSession(Caravan caravan)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.caravan = caravan;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");

            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        }
        public Transferable GetTransferableByThingId(int thingId)
        {
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        public void Notify_CountChanged(Transferable tr)
        {
            uiDirty = true;
        }
    }

    class MpCaravanSplitWindow : Window
    {
        private Caravan caravan;
        private Dialog_SplitCaravan dialog;

        public MpCaravanSplitWindow(Caravan caravan)
        {
            this.caravan = caravan;
        }

        public override void DoWindowContents(Rect inRect)
        {
            dialog.DoWindowContents(inRect);
        }

        private void CreateDialog()
        {
            CancelDialogSplitCaravanCtor.cancel = true;

            dialog = new Dialog_SplitCaravan(null);

            CancelDialogSplitCaravanCtor.cancel = false;
        }
    }
}
