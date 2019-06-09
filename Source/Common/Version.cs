namespace Multiplayer.Common
{
    public static class MpVersion
    {
        public const string Version = "0.4.7.2-nf";
        public const int Protocol = 17;

#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
    }
}
