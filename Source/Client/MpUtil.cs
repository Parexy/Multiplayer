#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;
using UnityEngine;
using Verse;

#endregion

namespace Multiplayer.Client
{
    public static class MpUtil
    {
        private static Func<ICustomAttributeProvider, Type, bool> IsDefinedInternal;

        private static readonly List<MethodBase> methods = new List<MethodBase>(10);
        private static int depth;
        private static IntPtr upToHandle;
        private static int max;
        private static readonly IntPtr walkPtr = Marshal.GetFunctionPointerForDelegate((walk_stack) WalkStack);
        private static Func<IntPtr, MethodBase> methodHandleToMethodBase;

        // Doesn't load the type
        public static bool HasAttr(ICustomAttributeProvider provider, Type attrType)
        {
            if (IsDefinedInternal == null)
                IsDefinedInternal = (Func<ICustomAttributeProvider, Type, bool>) Delegate.CreateDelegate(
                    typeof(Func<ICustomAttributeProvider, Type, bool>),
                    AccessTools.Method(Type.GetType("System.MonoCustomAttrs"), "IsDefinedInternal"));

            return IsDefinedInternal(provider, attrType);
        }

        public static string FixedEllipsis()
        {
            int num = Mathf.FloorToInt(Time.realtimeSinceStartup) % 3;
            if (num == 0)
                return ".  ";
            if (num == 1)
                return ".. ";
            return "...";
        }

        public static IEnumerable<Type> AllModTypes()
        {
            foreach (Assembly asm in LoadedModManager.RunningMods.SelectMany(m => m.assemblies.loadedAssemblies))
            {
                Type[] types = null;

                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    Log.Error($"Exception getting types in assembly {asm}");
                }

                if (types != null)
                    foreach (Type t in types)
                        yield return t;
            }
        }

        public static unsafe void MarkNoInlining(MethodBase method)
        {
            ushort* iflags = (ushort*) method.MethodHandle.Value + 1;
            *iflags |= (ushort) MethodImplOptions.NoInlining;
        }

        public static T UninitializedObject<T>()
        {
            return (T) FormatterServices.GetUninitializedObject(typeof(T));
        }

        // Copied from Harmony.PatchProcessor
        public static MethodBase GetOriginalMethod(HarmonyMethod attr)
        {
            if (attr.declaringType == null) return null;

            if (attr.methodType == null)
                attr.methodType = MethodType.Normal;

            switch (attr.methodType)
            {
                case MethodType.Normal:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes);

                case MethodType.Getter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetGetMethod(true);

                case MethodType.Setter:
                    if (attr.methodName == null)
                        return null;
                    return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName).GetSetMethod(true);

                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(attr.declaringType, attr.argumentTypes);

                case MethodType.StaticConstructor:
                    return AccessTools.GetDeclaredConstructors(attr.declaringType)
                        .Where(c => c.IsStatic)
                        .FirstOrDefault();
            }

            return null;
        }

        // https://stackoverflow.com/a/27376368
        public static string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint.Address.ToString();
                }
            }
            catch
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork).ToString();
            }
        }

        public static MethodBase MethodHandleToMethodBase(IntPtr methodHandle)
        {
            if (methodHandleToMethodBase == null)
            {
                DynamicMethod dyn = new DynamicMethod("MethodHandleToMethodBase", typeof(MethodBase),
                    new[] {typeof(IntPtr)});
                ILGenerator il = dyn.GetILGenerator();
                LocalBuilder local = il.DeclareLocal(typeof(RuntimeTypeHandle));

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Newobj, AccessTools.Constructor(typeof(RuntimeMethodHandle), new[] {typeof(IntPtr)}));
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Initobj, typeof(RuntimeTypeHandle));
                il.Emit(OpCodes.Ldloc_S, local);
                il.Emit(OpCodes.Call,
                    AccessTools.Method(typeof(MethodBase), nameof(MethodBase.GetMethodFromHandle),
                        new[] {typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)}));
                il.Emit(OpCodes.Ret);

                methodHandleToMethodBase =
                    (Func<IntPtr, MethodBase>) dyn.CreateDelegate(typeof(Func<IntPtr, MethodBase>));
            }

            return methodHandleToMethodBase(methodHandle);
        }

        // Not thread safe
        public static MethodBase[] FastStackTrace(int skip = 0, MethodBase upTo = null, int max = 0)
        {
            depth = 0;
            methods.Clear();

            MpUtil.max = max;

            upToHandle = IntPtr.Zero;
            if (upTo != null)
                upToHandle = upTo.MethodHandle.Value;

            Native.mono_stack_walk(walkPtr, (IntPtr) skip);

            return methods.ToArray();
        }

        private static bool WalkStack(IntPtr methodHandle, int native, int il, bool managed, IntPtr skip)
        {
            depth++;
            if (depth > (int) skip)
                methods.Add(MethodHandleToMethodBase(methodHandle));
            if (methodHandle == upToHandle || depth == max) return true;
            return false;
        }

        public static List<ILInstruction> GetInstructions(MethodBase method)
        {
            MethodBodyReader insts = new MethodBodyReader(method, null);
            insts.SetPropertyOrField("locals", null);
            insts.ReadInstructions();
            return (List<ILInstruction>) insts.GetPropertyOrField("ilInstructions");
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class HotSwappableAttribute : Attribute
    {
    }

    public struct Container<T>
    {
        public T Inner { get; }

        public Container(T value)
        {
            Inner = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
        }
    }

    public class OrderedDict<K, V> : IEnumerable
    {
        private readonly Dictionary<K, V> dict = new Dictionary<K, V>();
        private readonly List<K> list = new List<K>();

        public K this[int index] => list[index];

        public V this[K key] => dict[key];

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }

        public void Add(K key, V value)
        {
            dict.Add(key, value);
            list.Add(key);
        }

        public void Insert(int index, K key, V value)
        {
            dict.Add(key, value);
            list.Insert(index, key);
        }

        public bool TryGetValue(K key, out V value)
        {
            value = default;
            return dict.TryGetValue(key, out value);
        }
    }

    public class UniqueList<T> : IEnumerable<T>
    {
        private readonly List<T> list = new List<T>();
        private readonly HashSet<T> set = new HashSet<T>();

        public int Count => list.Count;
        public T this[int index] => list[index];

        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return list.GetEnumerator();
        }

        public bool Add(T t)
        {
            if (set.Add(t))
            {
                list.Add(t);
                return true;
            }

            return false;
        }

        public T[] ToArray()
        {
            return list.ToArray();
        }

        public bool Contains(T t)
        {
            return set.Contains(t);
        }

        public int IndexOf(T t)
        {
            return list.IndexOf(t);
        }
    }

    public class DefaultComparer<T> : IEqualityComparer<T>
    {
        public static DefaultComparer<T> Instance = new DefaultComparer<T>();

        public bool Equals(T x, T y)
        {
            return object.Equals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}