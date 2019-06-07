using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Harmony;
using Multiplayer.Client.Synchronization;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public class PersistentDialog : IExposable
    {
        public Dialog_NodeTreeWithFactionInfo dialog;
        private Faction faction;

        private List<FieldSave> fieldValues;
        public int id;
        public Map map;
        private bool radioMode;
        private List<DiaNodeSave> saveNodes;
        private string title;
        public int ver;

        public PersistentDialog(Map map)
        {
            this.map = map;
        }

        public PersistentDialog(Map map, Dialog_NodeTreeWithFactionInfo dialog) : this(map)
        {
            id = Multiplayer.GlobalIdBlock.NextId();
            this.dialog = dialog;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_References.Look(ref dialog.faction, "faction");
                ScribeUtil.LookValue(dialog.soundAmbient == SoundDefOf.RadioComms_Ambience, "radioMode");
                Scribe_Values.Look(ref dialog.title, "title");

                var nodes = dialog.curNode.TraverseNodes().ToList();

                saveNodes = new List<DiaNodeSave>();
                foreach (var node in nodes)
                    saveNodes.Add(new DiaNodeSave(this, node));

                fieldValues = nodes
                    .SelectMany(n => n.options)
                    .SelectMany(o => DelegateValues(o.action).Concat(DelegateValues(o.linkLateBind)))
                    .Distinct(new FieldSaveEquality())
                    .ToList();

                Scribe_Collections.Look(ref fieldValues, "fieldValues", LookMode.Deep);
                Scribe_Collections.Look(ref saveNodes, "nodes", LookMode.Deep);

                fieldValues = null;
                saveNodes = null;
            }
            else
            {
                Scribe_References.Look(ref faction, "faction");
                Scribe_Values.Look(ref radioMode, "radioMode");
                Scribe_Values.Look(ref title, "title");

                Scribe_Collections.Look(ref fieldValues, "fieldValues", LookMode.Deep, this);
                Scribe_Collections.Look(ref saveNodes, "nodes", LookMode.Deep, this);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dialog = new Dialog_NodeTreeWithFactionInfo(saveNodes[0].node, faction, false, radioMode, title)
                {
                    doCloseX = true,
                    closeOnCancel = true
                };

                faction = null;
                saveNodes = null;
                fieldValues = null;
            }
        }

        [SyncMethod]
        public void Click(int ver, int opt)
        {
            if (ver != this.ver) return;
            dialog.curNode.options[opt].Activate();
            this.ver++;
        }

        public static PersistentDialog FindDialog(Window dialog)
        {
            return Find.Maps.SelectMany(m => m.MpComp().mapDialogs).FirstOrDefault(d => d.dialog == dialog);
        }

        private IEnumerable<FieldSave> DelegateValues(Delegate del)
        {
            if (del == null) yield break;

            yield return new FieldSave(this, del.GetType(), del);

            var target = del.Target;
            if (target == null) yield break;

            yield return new FieldSave(this, target.GetType(), target);

            foreach (var field in target.GetType().GetDeclaredInstanceFields())
                if (!field.FieldType.IsCompilerGenerated())
                    yield return new FieldSave(this, field.FieldType, field.GetValue(target));
        }

        private class DiaNodeSave : IExposable
        {
            public readonly PersistentDialog parent;
            public DiaNode node;
            private List<DiaOptionSave> saveOptions;

            private string text;

            public DiaNodeSave(PersistentDialog parent)
            {
                this.parent = parent;
            }

            public DiaNodeSave(PersistentDialog parent, DiaNode node) : this(parent)
            {
                this.node = node;
            }

            public void ExposeData()
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    Scribe_Values.Look(ref node.text, "text");

                    var saveOptions = node.options.Select(o => new DiaOptionSave(parent, o)).ToList();
                    Scribe_Collections.Look(ref saveOptions, "options", LookMode.Deep);
                }


                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    Scribe_Values.Look(ref text, "text");
                    Scribe_Collections.Look(ref saveOptions, "options", LookMode.Deep, parent);

                    node = new DiaNode(text);
                }

                if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                    node.options = saveOptions.Select(o => o.opt).ToList();
            }
        }

        private class DiaOptionSave : IExposable
        {
            public readonly PersistentDialog parent;
            private int actionIndex;
            private SoundDef clickSound;
            private bool disabled;
            private string disabledReason;
            private int linkIndex;
            private int linkLateBindIndex;
            public DiaOption opt;
            private bool resolveTree;

            private string text;

            public DiaOptionSave(PersistentDialog parent)
            {
                this.parent = parent;
            }

            public DiaOptionSave(PersistentDialog parent, DiaOption opt) : this(parent)
            {
                this.opt = opt;
            }

            public void ExposeData()
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    Scribe_Values.Look(ref opt.text, "text");
                    Scribe_Values.Look(ref opt.resolveTree, "resolveTree");
                    ScribeUtil.LookValue(parent.saveNodes.FindIndex(n => n.node == opt.link), "linkIndex", true);
                    Scribe_Values.Look(ref opt.disabled, "disabled");
                    Scribe_Values.Look(ref opt.disabledReason, "disabledReason");
                    Scribe_Defs.Look(ref opt.clickSound, "clickSound");

                    ScribeUtil.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, opt.action)), "actionIndex", true);
                    ScribeUtil.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, opt.linkLateBind)), "linkLateBindIndex", true);
                }

                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    Scribe_Values.Look(ref text, "text");
                    Scribe_Values.Look(ref resolveTree, "resolveTree");
                    Scribe_Values.Look(ref linkIndex, "linkIndex", -1);
                    Scribe_Values.Look(ref disabled, "disabled");
                    Scribe_Values.Look(ref disabledReason, "disabledReason");
                    Scribe_Defs.Look(ref clickSound, "clickSound");

                    Scribe_Values.Look(ref actionIndex, "actionIndex");
                    Scribe_Values.Look(ref linkLateBindIndex, "linkLateBindIndex");

                    opt = new DiaOption
                    {
                        text = text,
                        resolveTree = resolveTree,
                        disabled = disabled,
                        disabledReason = disabledReason,
                        clickSound = clickSound
                    };
                }

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    opt.link = parent.saveNodes.ElementAtOrDefault(linkIndex)?.node;
                    opt.action = (Action) parent.fieldValues.ElementAtOrDefault(actionIndex)?.value;
                    opt.linkLateBind = (Func<DiaNode>) parent.fieldValues.ElementAtOrDefault(linkLateBindIndex)?.value;
                }
            }
        }

        private class FieldSave : IExposable
        {
            private static readonly MethodInfo ScribeValues = typeof(Scribe_Values).GetMethod("Look");
            private static readonly MethodInfo ScribeDefs = typeof(Scribe_Defs).GetMethod("Look");
            private static readonly MethodInfo ScribeReferences = typeof(Scribe_References).GetMethods().First(m => m.Name == "Look" && (m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericParameter ?? false));
            private static readonly MethodInfo ScribeDeep = typeof(Scribe_Deep).GetMethods().First(m => m.Name == "Look" && m.GetParameters().Length == 3);
            public readonly PersistentDialog parent;

            private Dictionary<string, int> fields;
            private string methodName;

            private string methodType;
            private LookMode mode;
            private int targetIndex;
            public Type type;
            public string typeName;
            public object value;

            public FieldSave(PersistentDialog parent)
            {
                this.parent = parent;
            }

            public FieldSave(PersistentDialog parent, Type type, object value) : this(parent)
            {
                this.type = type;
                typeName = type.FullName;
                this.value = value;

                if (typeof(Delegate).IsAssignableFrom(type))
                    mode = (LookMode) 101;
                else if (ParseHelper.HandlesType(type))
                    mode = LookMode.Value;
                else if (typeof(Def).IsAssignableFrom(type))
                    mode = LookMode.Def;
                else if (value is Thing thing && !thing.Spawned)
                    mode = LookMode.Deep;
                else if (typeof(ILoadReferenceable).IsAssignableFrom(type))
                    mode = LookMode.Reference;
                else if (typeof(IExposable).IsAssignableFrom(type))
                    mode = LookMode.Deep;
                else
                    mode = (LookMode) 100;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref mode, "mode");

                Scribe_Values.Look(ref typeName, "type");
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                    type = MpReflection.GetTypeByName(typeName);

                if (mode == LookMode.Value)
                {
                    var args = new[] {value, "value", type.GetDefaultValue(), false};
                    ScribeValues.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == LookMode.Def)
                {
                    var args = new[] {value, "value"};
                    ScribeDefs.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == LookMode.Reference)
                {
                    var args = new[] {value, "value", false};
                    ScribeReferences.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                        value = args[0];
                }
                else if (mode == LookMode.Deep)
                {
                    var args = new[] {value, "value", new object[0]};
                    ScribeDeep.MakeGenericMethod(type).Invoke(null, args);
                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                        value = args[0];
                }
                else if (mode == (LookMode) 100)
                {
                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        fields = new Dictionary<string, int>();
                        foreach (var field in type.GetDeclaredInstanceFields())
                            if (!field.FieldType.IsCompilerGenerated())
                                fields[field.Name] = parent.fieldValues.FindIndex(v => Equals(v.value, field.GetValue(value)));

                        Scribe_Collections.Look(ref fields, "fields");
                    }

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        value = Activator.CreateInstance(type);
                        Scribe_Collections.Look(ref fields, "fields");
                    }

                    if (Scribe.mode == LoadSaveMode.PostLoadInit)
                        foreach (var kv in fields)
                            value.SetPropertyOrField(kv.Key, parent.fieldValues[kv.Value].value);
                }
                else if (mode == (LookMode) 101)
                {
                    if (Scribe.mode == LoadSaveMode.Saving)
                    {
                        var del = (Delegate) value;

                        ScribeUtil.LookValue(del.Method.DeclaringType.FullName, "methodType");
                        ScribeUtil.LookValue(del.Method.Name, "methodName");

                        if (del.Target != null)
                            ScribeUtil.LookValue(parent.fieldValues.FindIndex(f => Equals(f.value, del.Target)), "targetIndex", true);
                    }

                    if (Scribe.mode == LoadSaveMode.LoadingVars)
                    {
                        Scribe_Values.Look(ref methodType, "methodType");
                        Scribe_Values.Look(ref methodName, "methodName");
                        Scribe_Values.Look(ref targetIndex, "targetIndex");
                    }

                    if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                    {
                        if (targetIndex != -1)
                        {
                            var target = parent.fieldValues[targetIndex].value;
                            value = Delegate.CreateDelegate(type, target, methodName);
                        }
                        else
                        {
                            value = Delegate.CreateDelegate(type, GenTypes.GetTypeInAnyAssemblyNew(methodType, null), methodName);
                        }
                    }
                }
            }
        }

        private class FieldSaveEquality : IEqualityComparer<FieldSave>
        {
            public bool Equals(FieldSave x, FieldSave y)
            {
                return Equals(x.value, y.value);
            }

            public int GetHashCode(FieldSave obj)
            {
                return obj.value?.GetHashCode() ?? 0;
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class CancelDialogNodeTree
    {
        private static bool Prefix(Window window)
        {
            var map = Multiplayer.MapContext;

            if (map != null && window.GetType() == typeof(Dialog_NodeTreeWithFactionInfo))
            {
                var dialog = (Dialog_NodeTreeWithFactionInfo) window;
                dialog.doCloseX = true;
                dialog.closeOnCancel = true;

                map.MpComp().mapDialogs.Add(new PersistentDialog(map, dialog));
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PreClose))]
    internal static class DialogNodeTreePreClose
    {
        private static bool Prefix(Dialog_NodeTree __instance)
        {
            return Multiplayer.Client == null || __instance.GetType() != typeof(Dialog_NodeTreeWithFactionInfo) || !Multiplayer.ShouldSync;
        }
    }

    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.PostClose))]
    internal static class DialogNodeTreePostClose
    {
        private static bool Prefix(Dialog_NodeTree __instance)
        {
            if (Multiplayer.Client == null) return true;
            if (__instance.GetType() != typeof(Dialog_NodeTreeWithFactionInfo)) return true;

            var session = PersistentDialog.FindDialog(__instance);
            if (session != null && Multiplayer.ShouldSync)
            {
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.TryRemove), typeof(Window), typeof(bool))]
    internal static class WindowStackTryRemove
    {
        private static void Postfix(Window window)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.ShouldSync) return;
            if (window.GetType() != typeof(Dialog_NodeTreeWithFactionInfo)) return;

            var session = PersistentDialog.FindDialog(window);
            session.map.MpComp().mapDialogs.Remove(session);
        }
    }

    [HarmonyPatch(typeof(DiaOption), nameof(DiaOption.Activate))]
    internal static class DiaOptionActivate
    {
        private static bool Prefix(DiaOption __instance)
        {
            if (Multiplayer.ShouldSync)
            {
                var session = PersistentDialog.FindDialog(__instance.dialog);
                if (session == null) return true;

                session.Click(session.ver, session.dialog.curNode.options.IndexOf(__instance));

                return false;
            }

            return true;
        }
    }
}