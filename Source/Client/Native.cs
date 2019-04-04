#region

using System;
using System.Runtime.InteropServices;

#endregion

namespace Multiplayer.Client
{
    internal delegate bool walk_stack(IntPtr methodHandle, int native, int il, bool managed, IntPtr data);

    internal static class Native
    {
        [DllImport("mono.dll")]
        private static extern bool mono_debug_using_mono_debugger();

        [DllImport("mono.dll")]
        private static extern IntPtr
            mono_debug_print_stack_frame(IntPtr methodHandle, int codePtr, IntPtr domainHandle);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_domain_get();

        [DllImport("mono.dll")]
        private static extern IntPtr mono_debug_init(int format);

        [DllImport("mono.dll")]
        public static extern void mono_set_defaults(IntPtr verboseLevel, int opts);

        [DllImport("mono.dll")]
        private static extern IntPtr
            mono_debug_open_image_from_memory(IntPtr imageHandle, IntPtr contents, IntPtr size);

        [DllImport("mono.dll")]
        private static extern IntPtr mono_debug_find_method(IntPtr methodHandle, IntPtr domainHandle);

        [DllImport("mono.dll")]
        private static extern IntPtr mono_class_get_image(IntPtr classHandle);

        [DllImport("mono.dll")]
        private static extern IntPtr mono_type_get_class(IntPtr typeHandle);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_valloc(IntPtr addr, IntPtr length, IntPtr flags);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_method_get_header(IntPtr methodHandle);

        [DllImport("mono.dll")]
        public static extern unsafe int mono_method_get_flags(IntPtr methodHandle, int* iflags);

        [DllImport("mono.dll")]
        public static extern IntPtr mono_method_header_get_code(IntPtr header, IntPtr codeSize, IntPtr maxStack);

        [DllImport("mono.dll")]
        public static extern void mono_stack_walk(IntPtr walkFunc, IntPtr data);
    }
}