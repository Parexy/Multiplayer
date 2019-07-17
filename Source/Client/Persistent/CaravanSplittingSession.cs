using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Multiplayer.API;
using Verse.Sound;

namespace Multiplayer.Client.Persistent
{
    /// <summary> 
    /// Represents an active Caravan Split session. This session will track all the pawns and items being split.
    /// </summary>
    public class CaravanSplittingSession : IExposable, ISessionWithTransferables
    {
        private int sessionId;
        public int SessionId => sessionId;
        public List<TransferableOneWay> transferables;
        public bool uiDirty;
        public Caravan caravan;
        public CaravanSplitting_Proxy dialog;

        public CaravanSplittingSession(Caravan caravan)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.caravan = caravan;

            AddItems();
        }

        private void AddItems()
        {
            dialog = new CaravanSplitting_Proxy(caravan) {
                session = this
            };
            dialog.CalculateAndRecacheTransferables();
            transferables = dialog.transferables;

            Find.WindowStack.Add(dialog);
        }

        public void OpenWindow(bool sound = true)
        {
            Find.Selector.ClearSelection();

            dialog = PrepareDialogProxy();
            if (!sound)
                dialog.soundAppear = null;
            dialog.doCloseX = true;

            CaravanUIUtility.CreateCaravanTransferableWidgets(transferables, out dialog.pawnsTransfer, out dialog.itemsTransfer, "SplitCaravanThingCountTip".Translate(), IgnorePawnsInventoryMode.Ignore, () => dialog.DestMassCapacity - dialog.DestMassUsage, false, caravan.Tile, false);
            dialog.CountToTransferChanged();

            Find.WindowStack.Add(dialog);
        }

        private CaravanSplitting_Proxy PrepareDialogProxy()
        {
            var dialog = new CaravanSplitting_Proxy(caravan)
            {
                transferables = transferables,
                session = this
            };

            return dialog;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");

            Scribe_Collections.Look(ref transferables, "transferables", LookMode.Deep);
        }

        public Transferable GetTransferableByThingId(int thingId)
        {
            Multiplayer.session.AddMsg($"GetTransferableByThingId {thingId}", true);
            return transferables.Find(tr => tr.things.Any(t => t.thingIDNumber == thingId));
        }

        public void Notify_CountChanged(Transferable tr)
        {
            Multiplayer.session.AddMsg($"Caravan splitting session - Notify_CountChanged", true);
            uiDirty = true;
        }
        
        [SyncMethod]
        public static void CreateSplittingSession(Caravan caravan)
        {
            //start caravan spltting session here by calling new session constructor

            if (Multiplayer.WorldComp.splitSession == null)
            {
                Multiplayer.session.AddMsg("Creating  Multiplayer.WorldComp.splitSession", true);
                Multiplayer.WorldComp.splitSession = new CaravanSplittingSession(caravan);
            }
        }

        [SyncMethod]
        public static void CancelSplittingSession() {
            if (Multiplayer.WorldComp.splitSession != null) {
                Multiplayer.WorldComp.splitSession.dialog.Close(true);
                Multiplayer.WorldComp.splitSession = null;
            }
        }

        [SyncMethod]
        public static void ResetSplittingSession()
        {
            Multiplayer.WorldComp.splitSession.transferables.ForEach(t => t.CountToTransfer = 0);
            Multiplayer.WorldComp.splitSession.uiDirty = true;
        }

        [SyncMethod]
        public static void AcceptSplitSession()
        {
            if (Multiplayer.WorldComp.splitSession != null)
            {
                if (Multiplayer.WorldComp.splitSession.dialog.TrySplitCaravan())
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    Multiplayer.WorldComp.splitSession.dialog.Close(false);
                    Multiplayer.WorldComp.splitSession = null;
                }
            }
        }
    }
}
