﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    public enum JoiningState
    {
        Connected, Downloading
    }

    public interface IConnectionStatusListener
    {
        void Connected();
        void Disconnected();
    }

    public static class ConnectionStatusListeners
    {
        private static IEnumerable<IConnectionStatusListener> All
        {
            get
            {
                if (Find.WindowStack != null)
                    foreach (Window window in Find.WindowStack.Windows.ToList())
                        if (window is IConnectionStatusListener listener)
                            yield return listener;

                if (Multiplayer.Client?.PacketHandler is IConnectionStatusListener state)
                    yield return state;

                if (Multiplayer.session != null)
                    yield return Multiplayer.session;
            }
        }

        public static void TryNotifyAll_Connected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Connected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }

        public static void TryNotifyAll_Disconnected()
        {
            foreach (var listener in All)
            {
                try
                {
                    listener.Disconnected();
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }
        }
    }

}
