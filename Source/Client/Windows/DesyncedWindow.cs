using System.Linq;
using System.Threading;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Networking.Handler;
using Multiplayer.Common.Networking;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Windows
{
    public class DesyncedWindow : Window
    {
        private readonly string text;
        public bool dataObtained = false;
        public string reportId;
        public bool reporting;
        public bool reportQuestionAnswered;

        public DesyncedWindow(string text)
        {
            this.text = text;

            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(550, 150);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0, 0, inRect.width, 40), $"{"MpDesynced".Translate()}\n{text}");
            Text.Anchor = TextAnchor.UpperLeft;

            //TODO: Translate
            if (!dataObtained)
            {
                //"We're just gathering some information from the other player, then we'll see what we can do to make this better"
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 40, inRect.width, 40), "MpGatheringDesyncReport".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (!reportQuestionAnswered)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 40, inRect.width, 40), "MpReportDesyncPrompt".Translate());
                Text.Anchor = TextAnchor.UpperLeft;

                float buttonWidth = 120 * 2 + 10;
                var buttonRect = new Rect((inRect.width - buttonWidth) / 2, 80, buttonWidth, 35);

                GUI.BeginGroup(buttonRect);

                float x = 0;
                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Yes"))
                {
                    //Report
                    reporting = true;
                    reportQuestionAnswered = true;

                    new Thread(DesyncReporter.Upload)
                    {
                        IsBackground = true
                    }.Start();
                }

                x += 120 + 10;

                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "No"))
                    reportQuestionAnswered = true; //Just skip this

                GUI.EndGroup();
            }
            else if (reporting)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 40, inRect.width, 40), "MpUploadingDesync".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                var y = 40;
                if (reportId != null)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0, 40, inRect.width, 40),
                        reportId.NullOrEmpty()
                            ? "MpReportFailed".Translate()
                            : $"```{reportId}```. {"MpReportSuccess".Translate()}");
                    y += 40;
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                float buttonWidth = 120 * 4 + 10 * 3;
                var buttonRect = new Rect((inRect.width - buttonWidth) / 2, y, buttonWidth, 35);

                GUI.BeginGroup(buttonRect);

                float x = 0;
                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpTryResync".Translate()))
                {
                    Multiplayer.session.resyncing = true;

                    TickPatch.SkipTo(
                        tickUntilCaughtUp: true,
                        onFinish: () =>
                        {
                            Multiplayer.session.resyncing = false;
                            Multiplayer.Client.Send(Packet.Client_WorldReady);
                        },
                        cancelButtonKey: "Quit",
                        onCancel: GenScene.GoToMainMenu
                    );

                    Multiplayer.session.desynced = false;

                    ClientHandshakePacketHandler.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
                }

                x += 120 + 10;

                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Save".Translate()))
                    Find.WindowStack.Add(new Dialog_SaveReplay());
                x += 120 + 10;

                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "MpChatButton".Translate()))
                    Find.WindowStack.Add(new ChatWindow {closeOnClickedOutside = true, absorbInputAroundWindow = true});
                x += 120 + 10;

                if (Widgets.ButtonText(new Rect(x, 0, 120, 35), "Quit".Translate()))
                    MainMenuPatch.AskQuitToMainMenu();

                GUI.EndGroup();
            }
        }
    }
}