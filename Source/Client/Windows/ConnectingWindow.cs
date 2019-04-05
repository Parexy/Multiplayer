#region

using Steamworks;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    public abstract class BaseConnectingWindow : Window, IConnectionStatusListener
    {
        protected string desc;

        protected string result;

        public bool returnToServerBrowser;

        public BaseConnectingWindow()
        {
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override Vector2 InitialSize => new Vector2(400f, 150f);

        public virtual bool IsConnecting => result == null;
        public abstract string ConnectingString { get; }

        public void Connected()
        {
            result = "MpConnected".Translate();
        }

        public void Disconnected()
        {
        }

        public override void DoWindowContents(Rect inRect)
        {
            var label = IsConnecting ? ConnectingString + MpUtil.FixedEllipsis() : result;

            if (Multiplayer.Client?.StateObj is ClientJoiningState joining && joining.state == JoiningState.Downloading)
                label = $"MpDownloading".Translate(Multiplayer.Client.FragmentProgress);

            const float buttonHeight = 40f;
            const float buttonWidth = 120f;

            var textRect = inRect;
            textRect.yMax -= buttonHeight + 10f;
            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(textRect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            var buttonRect = new Rect((inRect.width - buttonWidth) / 2f, inRect.height - buttonHeight - 10f,
                buttonWidth, buttonHeight);
            if (Widgets.ButtonText(buttonRect, "CancelButton".Translate(), true, false, true)) Close();
        }

        public override void PostClose()
        {
            OnMainThread.StopMultiplayer();

            if (returnToServerBrowser)
                Find.WindowStack.Add(new ServerBrowser());
        }
    }

    public class ConnectingWindow : BaseConnectingWindow
    {
        private readonly string address;
        private readonly int port;

        public ConnectingWindow(string address, int port)
        {
            this.address = address;
            this.port = port;

            ClientUtil.TryConnect(address, port);
        }

        public override string ConnectingString => string.Format("MpConnectingTo".Translate("{0}", port), address);
    }

    public class SteamConnectingWindow : BaseConnectingWindow
    {
        public string host;

        public CSteamID hostId;

        public SteamConnectingWindow(CSteamID hostId)
        {
            this.hostId = hostId;
            host = SteamFriends.GetFriendPersonaName(hostId);
        }

        public override string ConnectingString =>
            (host.NullOrEmpty() ? "" : $"{"MpSteamConnectingTo".Translate(host)}\n") +
            "MpSteamConnectingWaiting".Translate();
    }
}