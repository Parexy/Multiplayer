using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Persistent
{
    class CaravanSplitting_Proxy : Window
    {
        private Caravan caravan;
        private Dialog_SplitCaravan dialog;

        public CaravanSplitting_Proxy(Caravan caravan)
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
