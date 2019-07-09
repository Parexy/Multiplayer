using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Multiplayer.API;

namespace Multiplayer.Client.Persistent
{
    /// <summary> 
    /// Represents an active Caravan Split session. This session will track all the pawns and items being split.
    /// </summary>
    public class CaravanSplittingSession : IExposable, ISessionWithTransferables
    {
        private int sessionId;
        public int SessionId { get { return sessionId; } }
        public List<TransferableOneWay> transferables;
        public bool uiDirty;
        public Caravan caravan;
        public CaravanSplitting_Proxy dialog;

        public CaravanSplittingSession(Caravan caravan)
        {
            sessionId = Multiplayer.GlobalIdBlock.NextId();
            this.caravan = caravan;

            dialog = new CaravanSplitting_Proxy(caravan);
            Find.WindowStack.Add(dialog);
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
    }
}
