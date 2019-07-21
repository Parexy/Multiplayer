using Verse;
using RimWorld.Planet;
using Harmony;
using Multiplayer.API;

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
            //When not playing multiplayer, don't modify behavior.
            if (Multiplayer.Client == null) return true;

            //If the dialog being added is a native Dialog_SplitCaravan, cancel adding it to the window stack.
            if (window is Dialog_SplitCaravan && !(window is CaravanSplitting_Proxy))
            {
                return false;
            }

            //Otherwise, window being added is something else. Let it happen.
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

        static bool Prefix(Caravan caravan)
        {
            //When not playing multiplayer, don't modify behavior.
            if (Multiplayer.Client == null) return true;

            //If in the middle of processing a tick, don't modify behavior.
            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                return true;
            }

            //Otherwise cancel creation of the Dialog_SplitCaravan.
            //  If there's already an active session, open the window associated with it.
            //  Otherwise, create a new session.
            if (Multiplayer.WorldComp.splitSession != null)
            {
                Multiplayer.WorldComp.splitSession.OpenWindow();
            }
            else
            {
                CaravanSplittingSession.CreateSplittingSession(caravan);
            }

            return false;
        }
    }    
    
    /// <summary>
    /// When a Dialog_SplitCaravan would be constructed, cancel and construct a CaravanSplitting_Proxy instead.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan), nameof(Dialog_SplitCaravan.PostOpen))]
    class CancelDialogSplitCaravanPostOpen
    {
        static bool Prefix()
        {
            //When not playing multiplayer, don't modify behavior.
            if (Multiplayer.Client == null) return true;

            //Otherwise prevent the Dialog_SplitCaravan.PostOpen from executing.
            //This is needed to prevent the Dialog_SplitCaravan.CalculateAndRecacheTransferables from being called,
            //  since if it gets called the Dialog_SplitCaravan tranferrable list is replaced with a new one, 
            //  breaking the session's reference to the current list.
            return false;
        }
    }
}
