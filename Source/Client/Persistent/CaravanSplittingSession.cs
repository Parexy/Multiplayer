using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using RimWorld.Planet;

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

        public CaravanSplittingSession(Caravan caravan)
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
}
