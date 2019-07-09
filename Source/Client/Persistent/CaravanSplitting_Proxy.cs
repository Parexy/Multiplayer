﻿using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace Multiplayer.Client.Persistent
{
    public class CaravanSplitting_Proxy : Dialog_SplitCaravan
    {
        public CaravanSplitting_Proxy(Caravan caravan) : base(caravan)
        {
            this.caravan = caravan;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect rect = new Rect(0f, 0f, inRect.width, 35f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "SplitCaravan".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            CaravanUIUtility.DrawCaravanInfo(new CaravanUIUtility.CaravanInfo(SourceMassUsage, SourceMassCapacity, cachedSourceMassCapacityExplanation, SourceTilesPerDay, cachedSourceTilesPerDayExplanation, SourceDaysWorthOfFood, SourceForagedFoodPerDay, cachedSourceForagedFoodPerDayExplanation, SourceVisibility, cachedSourceVisibilityExplanation, -1f, -1f, null), new CaravanUIUtility.CaravanInfo(DestMassUsage, DestMassCapacity, cachedDestMassCapacityExplanation, DestTilesPerDay, cachedDestTilesPerDayExplanation, DestDaysWorthOfFood, DestForagedFoodPerDay, cachedDestForagedFoodPerDayExplanation, DestVisibility, cachedDestVisibilityExplanation, -1f, -1f, null), caravan.Tile, (!caravan.pather.Moving) ? null : new int?(TicksToArrive), -9999f, new Rect(12f, 35f, inRect.width - 24f, 40f), true, null, false);
            tabsList.Clear();
            tabsList.Add(new TabRecord("PawnsTab".Translate(), delegate
            {
                tab = Tab.Pawns;
            }, tab == Tab.Pawns));
            tabsList.Add(new TabRecord("ItemsTab".Translate(), delegate
            {
                tab = Tab.Items;
            }, tab == Tab.Items));
            inRect.yMin += 119f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, tabsList, 200f);
            inRect = inRect.ContractedBy(17f);
            GUI.BeginGroup(inRect);
            Rect rect2 = inRect.AtZero();
            DoBottomButtons(rect2);
            Rect inRect2 = rect2;
            inRect2.yMax -= 59f;
            bool flag = false;
            switch (tab)
            {
                case Tab.Pawns:
                    pawnsTransfer.OnGUI(inRect2, out flag);
                    break;
                case Tab.Items:
                    itemsTransfer.OnGUI(inRect2, out flag);
                    break;
            }
            if (flag)
            {
                CountToTransferChanged();
            }
            GUI.EndGroup();
        }

        public new void DoBottomButtons(Rect rect)
        {
            float num = rect.width / 2f;
            Vector2 bottomButtonSize = BottomButtonSize;
            float x = num - bottomButtonSize.x / 2f;
            float y = rect.height - 55f;
            Vector2 bottomButtonSize2 = BottomButtonSize;
            float x2 = bottomButtonSize2.x;
            Vector2 bottomButtonSize3 = BottomButtonSize;
            Rect rect2 = new Rect(x, y, x2, bottomButtonSize3.y);
            if (Widgets.ButtonText(rect2, "AcceptButton".Translate(), true, false, true) && TrySplitCaravan())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                Close(false);
            }
            float num2 = rect2.x - 10f;
            Vector2 bottomButtonSize4 = BottomButtonSize;
            float x3 = num2 - bottomButtonSize4.x;
            float y2 = rect2.y;
            Vector2 bottomButtonSize5 = BottomButtonSize;
            float x4 = bottomButtonSize5.x;
            Vector2 bottomButtonSize6 = BottomButtonSize;
            Rect rect3 = new Rect(x3, y2, x4, bottomButtonSize6.y);
            if (Widgets.ButtonText(rect3, "ResetButton".Translate(), true, false, true))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                CalculateAndRecacheTransferables();
            }
            float x5 = rect2.xMax + 10f;
            float y3 = rect2.y;
            Vector2 bottomButtonSize7 = BottomButtonSize;
            float x6 = bottomButtonSize7.x;
            Vector2 bottomButtonSize8 = BottomButtonSize;
            Rect rect4 = new Rect(x5, y3, x6, bottomButtonSize8.y);
            if (Widgets.ButtonText(rect4, "CancelButton".Translate(), true, false, true))
            {
                CancelButtonClicked(); 
            }
        }

        private void CancelButtonClicked() {
            CaravanSplittingSession.CancelSplittingSession();
        }
    }
}
