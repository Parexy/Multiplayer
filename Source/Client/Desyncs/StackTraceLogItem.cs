using System.Reflection;

namespace Multiplayer.Client.Desyncs
{
    public class StackTraceLogItem
    {
        public MethodBase[] stackTrace;
        public string additionalInfo;
    }
}