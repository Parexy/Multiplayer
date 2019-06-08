using System.Reflection;

namespace Multiplayer.Client.Synchronization
{
    public static class SyncTemplates
    {
        private static bool General(MethodBase method, object instance, object[] args)
        {
            if (Multiplayer.ShouldSync)
            {
                Sync.syncMethods[method].DoSync(instance, args);
                return false;
            }

            return true;
        }

        private static bool Prefix_0(MethodBase __originalMethod, object __instance)
        {
            return General(__originalMethod, __instance, new object[0]);
        }

        private static bool Prefix_1(MethodBase __originalMethod, object __instance, object __0)
        {
            return General(__originalMethod, __instance, new[] {__0});
        }

        private static bool Prefix_2(MethodBase __originalMethod, object __instance, object __0, object __1)
        {
            return General(__originalMethod, __instance, new[] {__0, __1});
        }

        private static bool Prefix_3(MethodBase __originalMethod, object __instance, object __0, object __1, object __2)
        {
            return General(__originalMethod, __instance, new[] {__0, __1, __2});
        }

        private static bool Prefix_4(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3)
        {
            return General(__originalMethod, __instance, new[] {__0, __1, __2, __3});
        }

        private static bool Prefix_5(MethodBase __originalMethod, object __instance, object __0, object __1, object __2, object __3, object __4)
        {
            return General(__originalMethod, __instance, new[] {__0, __1, __2, __3, __4});
        }
    }
}