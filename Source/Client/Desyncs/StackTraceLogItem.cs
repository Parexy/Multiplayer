using System.Reflection;

namespace Multiplayer.Client.Desyncs
{
    public class StackTraceLogItem
    {
        public string additionalInfo;

        public int lastValidTick = Multiplayer.game.sync.lastValidTick;
        public MethodBase[] stackTrace;
    }
}