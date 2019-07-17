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
            if (Multiplayer.Client == null) return true;

            if (window is Dialog_SplitCaravan && !(window is CaravanSplitting_Proxy))
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

        static bool Prefix(Caravan caravan)
        {
            if (Multiplayer.Client == null) return true;

            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                return true;
            }

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
            return false;
        }
    }
}
