using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Harmony;

namespace Multiplayer.Client.Persistent
{
    /// <summary>
    /// When a Dialog_SplitCaravan would be added to the window stack in multiplayer mode, cancel it.
    /// </summary>
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

    /// <summary>
    /// When a Dialog_SplitCaravan would be constructed, cancel and construct a CaravanSplitting_Proxy instead.
    /// </summary>
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
                Multiplayer.WorldComp.splitSession = new CaravanSplittingSession(caravan);

            if (TickPatch.currentExecutingCmdIssuedBySelf)
                Find.WindowStack.Add(new CaravanSplitting_Proxy(caravan));

            return true;
        }
    }
}
