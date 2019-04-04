#region

using System.Collections.Generic;
using System.Linq;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterOnGUI))]
    internal static class DrawPlayerCursors
    {
        private static void Postfix()
        {
            if (Multiplayer.Client == null || !MultiplayerMod.settings.showCursors || TickPatch.Skipping) return;

            int curMap = Find.CurrentMap.Index;

            foreach (PlayerInfo player in Multiplayer.session.players)
            {
                if (player.username == Multiplayer.username) continue;
                if (player.map != curMap) continue;

                GUI.color = new Color(1, 1, 1, 0.5f);
                Vector2 pos = Vector3.Lerp(player.lastCursor, player.cursor,
                    (float) (Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt) / 50f).MapToUIPosition();

                Texture2D icon = Multiplayer.icons.ElementAtOrDefault(player.cursorIcon);
                Texture2D drawIcon = icon ? icon : CustomCursor.CursorTex;
                Rect iconRect = new Rect(pos, new Vector2(24f * drawIcon.width / drawIcon.height, 24f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(
                    new Rect(pos, new Vector2(100, 30)).CenterOn(iconRect).Down(20f).Left(icon != null ? 0f : 5f),
                    player.username);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (icon != null && Multiplayer.iconInfos[player.cursorIcon].hasStuff)
                    GUI.color = new Color(0.5f, 0.4f, 0.26f, 0.5f); // Stuff color for wood

                GUI.DrawTexture(iconRect, drawIcon);

                if (player.dragStart != PlayerInfo.Invalid)
                {
                    GUI.color = new Color(1, 1, 1, 0.2f);
                    Widgets.DrawBox(new Rect {min = player.dragStart.MapToUIPosition(), max = pos}, 2);
                }

                GUI.color = Color.white;
            }
        }
    }

    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays))]
    [StaticConstructorOnStartup]
    internal static class SelectionBoxPatch
    {
        private static readonly Material GraySelection = MaterialPool.MatFrom("UI/Overlays/SelectionBracket",
            ShaderDatabase.MetaOverlay, new Color(0.9f, 0.9f, 0.9f, 0.5f));

        private static readonly HashSet<int> drawnThisUpdate = new HashSet<int>();
        private static readonly Dictionary<object, float> selTimes = new Dictionary<object, float>();

        private static void Postfix()
        {
            if (Multiplayer.Client == null || TickPatch.Skipping) return;

            foreach (Thing t in Find.Selector.SelectedObjects.OfType<Thing>())
                drawnThisUpdate.Add(t.thingIDNumber);

            foreach (PlayerInfo player in Multiplayer.session.players)
            foreach (KeyValuePair<int, float> sel in player.selectedThings)
            {
                if (!drawnThisUpdate.Add(sel.Key)) continue;
                if (!ThingsById.thingsById.TryGetValue(sel.Key, out Thing thing)) continue;
                if (thing.Map != Find.CurrentMap) continue;

                selTimes[thing] = sel.Value;
                SelectionDrawerUtility.CalculateSelectionBracketPositionsWorld(SelectionDrawer.bracketLocs, thing,
                    thing.DrawPos, thing.RotatedSize.ToVector2(), selTimes, Vector2.one);
                selTimes.Clear();

                for (int i = 0; i < 4; i++)
                {
                    Quaternion rotation = Quaternion.AngleAxis(-i * 90, Vector3.up);
                    Graphics.DrawMesh(MeshPool.plane10, SelectionDrawer.bracketLocs[i], rotation, GraySelection, 0);
                }
            }

            drawnThisUpdate.Clear();
        }
    }

    [HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DrawInspectStringFor))]
    internal static class DrawInspectPaneStringMarker
    {
        public static ISelectable drawingFor;

        private static void Prefix(ISelectable sel)
        {
            drawingFor = sel;
        }

        private static void Postfix()
        {
            drawingFor = null;
        }
    }

    [HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DrawInspectString))]
    internal static class DrawInspectStringPatch
    {
        private static void Prefix(ref string str)
        {
            if (Multiplayer.Client == null) return;
            if (!(DrawInspectPaneStringMarker.drawingFor is Thing thing)) return;

            List<string> players = new List<string>();

            foreach (PlayerInfo player in Multiplayer.session.players)
                if (player.selectedThings.ContainsKey(thing.thingIDNumber))
                    players.Add(player.username);

            if (players.Count > 0)
                str += $"\nSelected by: {players.Join()}";
        }
    }
}