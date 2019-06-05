using System;

namespace Multiplayer.Common
{
    public enum DefCheckStatus : byte
    {
        OK,
        Not_Found,
        Count_Diff,
        Hash_Diff,
    }

    public enum PlayerListAction : byte
    {
        List,
        Add,
        Remove,
        Latencies,
        Status
    }
}
