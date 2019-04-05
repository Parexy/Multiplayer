#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

#endregion

namespace Multiplayer.Client
{
    public static partial class Sync
    {
        private static readonly ReaderDictionary readers = new ReaderDictionary
        {
            {data => data.ReadByte()},
            {data => data.ReadSByte()},
            {data => data.ReadShort()},
            {data => data.ReadUShort()},
            {data => data.ReadInt32()},
            {data => data.ReadUInt32()},
            {data => data.ReadLong()},
            {data => data.ReadULong()},
            {data => data.ReadFloat()},
            {data => data.ReadDouble()},
            {data => data.ReadBool()},
            {data => data.ReadString()},
            {data => (Event) null},
            {data => ReadSync<Pawn>(data)?.mindState?.priorityWork},
            {data => ReadSync<Pawn>(data)?.playerSettings},
            {data => ReadSync<Pawn>(data)?.timetable},
            {data => ReadSync<Pawn>(data)?.workSettings},
            {data => ReadSync<Pawn>(data)?.drafter},
            {data => ReadSync<Pawn>(data)?.jobs},
            {data => ReadSync<Pawn>(data)?.outfits},
            {data => ReadSync<Pawn>(data)?.drugs},
            {data => ReadSync<Pawn>(data)?.foodRestriction},
            {data => ReadSync<Pawn>(data)?.training},
            {data => ReadSync<Pawn>(data)?.story},
            {data => ReadSync<Pawn>(data)?.outfits?.forcedHandler},
            {data => ReadSync<Caravan>(data)?.pather},
            {data => new FloatRange(data.ReadFloat(), data.ReadFloat())},
            {data => new IntRange(data.ReadInt32(), data.ReadInt32())},
            {data => new QualityRange(ReadSync<QualityCategory>(data), ReadSync<QualityCategory>(data))},
            {data => new NameSingle(data.ReadString(), data.ReadBool())},
            {data => new NameTriple(data.ReadString(), data.ReadString(), data.ReadString())},
            {data => new Rot4(data.ReadByte())},
            {data => new ITab_Bills()},
            {data => new ITab_Pawn_Gear()},
            {data => new ITab_TransporterContents()},
            {data => Current.Game.outfitDatabase},
            {data => Current.Game.drugPolicyDatabase},
            {data => Current.Game.foodRestrictionDatabase},
            {data => data.MpContext().map.areaManager},
            {data => data.MpContext().map.MpComp()},
            {data => Find.WorldSelector},
            {data => Find.Storyteller},
            {
                data =>
                {
                    var id = data.ReadInt32();
                    return data.MpContext().map.MpComp().mapDialogs.FirstOrDefault(s => s.id == id);
                }
            },
            {
                data =>
                {
                    var id = data.ReadInt32();
                    return Multiplayer.WorldComp.trading.FirstOrDefault(s => s.sessionId == id);
                }
            },
            {
                data =>
                {
                    var id = data.ReadInt32();
                    var session = data.MpContext().map.MpComp().caravanForming;
                    return session?.sessionId == id ? session : null;
                }
            },
            {
                data =>
                {
                    var id = data.ReadInt32();
                    var session = data.MpContext().map.MpComp().transporterLoading;
                    return session?.sessionId == id ? session : null;
                }
            },
            {
                data =>
                {
                    var hasThing = data.ReadBool();
                    if (hasThing)
                        return new LocalTargetInfo(ReadSync<Thing>(data));
                    else
                        return new LocalTargetInfo(ReadSync<IntVec3>(data));
                }
            }
        };

        private static readonly WriterDictionary writers = new WriterDictionary
        {
            {(ByteWriter data, byte b) => data.WriteByte(b)},
            {(ByteWriter data, sbyte b) => data.WriteSByte(b)},
            {(ByteWriter data, short u) => data.WriteShort(u)},
            {(ByteWriter data, ushort u) => data.WriteUShort(u)},
            {(ByteWriter data, int i) => data.WriteInt32(i)},
            {(ByteWriter data, uint i) => data.WriteUInt32(i)},
            {(ByteWriter data, long l) => data.WriteLong(l)},
            {(ByteWriter data, ulong l) => data.WriteULong(l)},
            {(ByteWriter data, float f) => data.WriteFloat(f)},
            {(ByteWriter data, double d) => data.WriteDouble(d)},
            {(ByteWriter data, bool b) => data.WriteBool(b)},
            {(ByteWriter data, string s) => data.WriteString(s)},
            {(ByteWriter data, Event e) => { }},
            {(ByteWriter data, PriorityWork work) => WriteSync(data, work.pawn)},
            {(ByteWriter data, Pawn_PlayerSettings comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_TimetableTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_DraftController comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_WorkSettings comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_JobTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_OutfitTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_DrugPolicyTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_FoodRestrictionTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_TrainingTracker comp) => WriteSync(data, comp.pawn)},
            {(ByteWriter data, Pawn_StoryTracker comp) => WriteSync(data, comp.pawn)},
            {
                (ByteWriter data, OutfitForcedHandler comp) =>
                    WriteSync(data, comp.forcedAps.Select(a => a.Wearer).FirstOrDefault())
            }, // this is fine, theoretically
            {(ByteWriter data, Caravan_PathFollower follower) => WriteSync(data, follower.caravan)},
            {
                (ByteWriter data, FloatRange range) =>
                {
                    data.WriteFloat(range.min);
                    data.WriteFloat(range.max);
                }
            },
            {
                (ByteWriter data, IntRange range) =>
                {
                    data.WriteInt32(range.min);
                    data.WriteInt32(range.max);
                }
            },
            {
                (ByteWriter data, QualityRange range) =>
                {
                    WriteSync(data, range.min);
                    WriteSync(data, range.max);
                }
            },
            {
                (ByteWriter data, NameSingle name) =>
                {
                    data.WriteString(name.nameInt);
                    data.WriteBool(name.numerical);
                }
            },
            {
                (ByteWriter data, NameTriple name) =>
                {
                    data.WriteString(name.firstInt);
                    data.WriteString(name.nickInt);
                    data.WriteString(name.lastInt);
                }
            },
            {(ByteWriter data, Rot4 rot) => data.WriteByte(rot.AsByte)},
            {(ByteWriter data, ITab_Bills tab) => { }},
            {(ByteWriter data, ITab_Pawn_Gear tab) => { }},
            {(ByteWriter data, ITab_TransporterContents tab) => { }},
            {(ByteWriter data, OutfitDatabase db) => { }},
            {(ByteWriter data, DrugPolicyDatabase db) => { }},
            {(ByteWriter data, FoodRestrictionDatabase db) => { }},
            {(ByteWriter data, AreaManager areas) => data.MpContext().map = areas.map},
            {(ByteWriter data, MultiplayerMapComp comp) => data.MpContext().map = comp.map},
            {(ByteWriter data, WorldSelector selector) => { }},
            {(ByteWriter data, Storyteller storyteller) => { }},
            {
                (ByteWriter data, PersistentDialog session) =>
                {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.id);
                }
            },
            {(ByteWriter data, MpTradeSession session) => data.WriteInt32(session.sessionId)},
            {
                (ByteWriter data, CaravanFormingSession session) =>
                {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.sessionId);
                }
            },
            {
                (ByteWriter data, TransporterLoading session) =>
                {
                    data.MpContext().map = session.map;
                    data.WriteInt32(session.sessionId);
                }
            },
            {
                (ByteWriter data, LocalTargetInfo info) =>
                {
                    data.WriteBool(info.HasThing);
                    if (info.HasThing)
                        WriteSync(data, info.Thing);
                    else
                        WriteSync(data, info.Cell);
                }
            }
        };

        public static Type[] storageParents;
        public static Type[] plantToGrowSettables;

        public static MultiTarget thingFilterTarget = new MultiTarget()
        {
            {typeof(IStoreSettingsParent), "GetStoreSettings/filter"},
            {typeof(Bill), "ingredientFilter"},
            {typeof(Outfit), "filter"},
            {typeof(FoodRestriction), "filter"}
        };

        public static Type[] thingCompTypes;
        public static Type[] designatorTypes;
        public static Type[] worldObjectCompTypes;

        public static Type[] gameCompTypes;
        public static Type[] worldCompTypes;
        public static Type[] mapCompTypes;

        private static readonly Type[] supportedThingHolders = new[]
        {
            typeof(Map),
            typeof(Thing),
            typeof(ThingComp),
            typeof(WorldObject),
            typeof(WorldObjectComp)
        };

        private static readonly MethodInfo ReadExposable =
            AccessTools.Method(typeof(ScribeUtil), nameof(ScribeUtil.ReadExposable));

        private static readonly MethodInfo GetDefByIdMethod = AccessTools.Method(typeof(Sync), nameof(GetDefById));

        private static Type[] AllImplementations(Type type)
        {
            return GenTypes.AllTypes
                .Where(t => t != type && type.IsAssignableFrom(t))
                .OrderBy(t => t.IsInterface)
                .ToArray();
        }

        public static void CollectTypes()
        {
            storageParents = AllImplementations(typeof(IStoreSettingsParent));
            plantToGrowSettables = AllImplementations(typeof(IPlantToGrowSettable));

            thingCompTypes = typeof(ThingComp).AllSubclassesNonAbstract().ToArray();
            designatorTypes = typeof(Designator).AllSubclassesNonAbstract().ToArray();
            worldObjectCompTypes = typeof(WorldObjectComp).AllSubclassesNonAbstract().ToArray();

            gameCompTypes = typeof(GameComponent).AllSubclassesNonAbstract().ToArray();
            worldCompTypes = typeof(WorldComponent).AllSubclassesNonAbstract().ToArray();
            mapCompTypes = typeof(MapComponent).AllSubclassesNonAbstract().ToArray();
        }

        public static T ReadSync<T>(ByteReader data)
        {
            return (T) ReadSyncObject(data, typeof(T));
        }

        private static T GetDefById<T>(ushort id) where T : Def, new()
        {
            return DefDatabase<T>.GetByShortHash(id);
        }

        public static object ReadSyncObject(ByteReader data, SyncType syncType)
        {
            var context = data.MpContext();
            var map = context.map;
            var type = syncType.type;

            try
            {
                if (typeof(object) == type)
                {
                    return null;
                }
                else if (type.IsByRef)
                {
                    return null;
                }
                else if (syncType.expose)
                {
                    if (!typeof(IExposable).IsAssignableFrom(type))
                        throw new SerializationException($"Type {type} can't be exposed because it isn't IExposable");

                    var exposableData = data.ReadPrefixedBytes();
                    return ReadExposable.MakeGenericMethod(type).Invoke(null, new[] {exposableData, null});
                }
                else if (typeof(IntVec3) == type)
                {
                    var y = data.ReadShort();
                    if (y < 0)
                        return IntVec3.Invalid;

                    var x = data.ReadShort();
                    var z = data.ReadShort();

                    return new IntVec3(x, y, z);
                }
                else if (readers.TryGetValue(type, out var reader))
                {
                    return reader(data);
                }
                else if (type.IsEnum)
                {
                    var enumType = Enum.GetUnderlyingType(type);
                    return readers[enumType](data);
                }
                else if (type.IsArray && type.GetArrayRank() == 1)
                {
                    var elementType = type.GetElementType();
                    var length = data.ReadUShort();
                    var arr = Array.CreateInstance(elementType, length);
                    for (var i = 0; i < length; i++)
                        arr.SetValue(ReadSyncObject(data, elementType), i);
                    return arr;
                }
                else if (type.IsGenericType)
                {
                    if (type.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var specialList = ReadSync<ListType>(data);
                        if (specialList == ListType.MapAllThings)
                            return map.listerThings.AllThings;
                        else if (specialList == ListType.MapAllDesignations)
                            return map.designationManager.allDesignations;

                        var listType = type.GetGenericArguments()[0];
                        var size = data.ReadUShort();
                        var list = (IList) Activator.CreateInstance(type, size);
                        for (var j = 0; j < size; j++)
                            list.Add(ReadSyncObject(data, listType));

                        return list;
                    }
                    else if (type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        var element = type.GetGenericArguments()[0];
                        return ReadSyncObject(data, typeof(List<>).MakeGenericType(element));
                    }
                    else if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        var isNull = data.ReadBool();
                        if (isNull) return null;

                        var hasValue = data.ReadBool();
                        if (!hasValue) return Activator.CreateInstance(type);

                        var nullableType = type.GetGenericArguments()[0];
                        return Activator.CreateInstance(type, ReadSyncObject(data, nullableType));
                    }
                }
                else if (typeof(Area).IsAssignableFrom(type))
                {
                    var areaId = data.ReadInt32();
                    if (areaId == -1)
                        return null;

                    return map.areaManager.AllAreas.Find(a => a.ID == areaId);
                }
                else if (typeof(Zone).IsAssignableFrom(type))
                {
                    var zoneId = data.ReadInt32();
                    if (zoneId == -1)
                        return null;

                    return map.zoneManager.AllZones.Find(zone => zone.ID == zoneId);
                }
                else if (typeof(Def).IsAssignableFrom(type))
                {
                    var shortHash = data.ReadUShort();
                    if (shortHash == 0)
                        return null;

                    var def = (Def) GetDefByIdMethod.MakeGenericMethod(type).Invoke(null, new object[] {shortHash});
                    if (def == null)
                        throw new Exception($"Couldn't find {type} with short hash {shortHash}");

                    return def;
                }
                else if (typeof(WITab_Caravan_Gear) == type)
                {
                    var hasThing = data.ReadBool();

                    Thing thing = null;
                    if (hasThing)
                    {
                        thing = ReadSync<Thing>(data);
                        if (thing == null)
                            return null;
                    }

                    var tab = new WITab_Caravan_Gear();
                    tab.draggedItem = thing;

                    return tab;
                }
                else if (typeof(PawnColumnWorker).IsAssignableFrom(type))
                {
                    var def = ReadSync<PawnColumnDef>(data);
                    return def.Worker;
                }
                else if (typeof(Command_SetPlantToGrow) == type)
                {
                    var settable = ReadSync<IPlantToGrowSettable>(data);
                    if (settable == null)
                        return null;

                    var settables = ReadSync<List<IPlantToGrowSettable>>(data);
                    settables.RemoveAll(s => s == null);

                    var command = MpUtil.UninitializedObject<Command_SetPlantToGrow>();
                    command.settable = settable;
                    command.settables = settables;

                    return command;
                }
                else if (typeof(Command_SetTargetFuelLevel) == type)
                {
                    var refuelables = ReadSync<List<CompRefuelable>>(data);
                    refuelables.RemoveAll(r => r == null);

                    var command = new Command_SetTargetFuelLevel();
                    command.refuelables = refuelables;

                    return command;
                }
                else if (typeof(Command_LoadToTransporter) == type)
                {
                    var transporter = ReadSync<CompTransporter>(data);
                    if (transporter == null)
                        return null;

                    var transporters = ReadSync<List<CompTransporter>>(data);
                    transporters.RemoveAll(r => r == null);

                    var command = new Command_LoadToTransporter();
                    command.transComp = transporter;
                    command.transporters = transporters;

                    return command;
                }
                else if (typeof(Designator).IsAssignableFrom(type))
                {
                    var desId = data.ReadUShort();
                    var desType = designatorTypes[desId];

                    Designator des;
                    if (desType == typeof(Designator_Build))
                    {
                        var def = ReadSync<BuildableDef>(data);
                        des = new Designator_Build(def);
                    }
                    else
                    {
                        des = (Designator) Activator.CreateInstance(desType);
                    }

                    return des;
                }
                else if (typeof(Thing).IsAssignableFrom(type))
                {
                    var thingId = data.ReadInt32();
                    if (thingId == -1)
                        return null;

                    if (!context.syncingThingParent)
                    {
                        var implIndex = data.ReadByte();
                        if (implIndex == byte.MaxValue)
                            return null;

                        var implType = supportedThingHolders[implIndex];

                        if (implType != typeof(Map))
                        {
                            context.syncingThingParent = true;
                            var parent = (IThingHolder) ReadSyncObject(data, implType);
                            context.syncingThingParent = false;

                            if (parent != null)
                                return ThingOwnerUtility.GetAllThingsRecursively(parent)
                                    .Find(t => t.thingIDNumber == thingId);
                            else
                                return null;
                        }
                    }

                    return ThingsById.thingsById.GetValueSafe(thingId);
                }
                else if (typeof(WorldObject).IsAssignableFrom(type))
                {
                    var objId = data.ReadInt32();
                    if (objId == -1)
                        return null;

                    return Find.World.worldObjects.AllWorldObjects.Find(w => w.ID == objId);
                }
                else if (typeof(WorldObjectComp).IsAssignableFrom(type))
                {
                    var compTypeId = data.ReadUShort();
                    if (compTypeId == ushort.MaxValue)
                        return null;

                    var parent = ReadSync<WorldObject>(data);
                    if (parent == null)
                        return null;

                    var compType = worldObjectCompTypes[compTypeId];
                    return parent.AllComps.Find(comp => comp.props.compClass == compType);
                }
                else if (typeof(CompChangeableProjectile) == type) // special case of ThingComp
                {
                    var parent = ReadSync<Thing>(data) as Building_TurretGun;
                    if (parent == null)
                        return null;

                    return (parent.gun as ThingWithComps).TryGetComp<CompChangeableProjectile>();
                }
                else if (typeof(ThingComp).IsAssignableFrom(type))
                {
                    var compTypeId = data.ReadUShort();
                    if (compTypeId == ushort.MaxValue)
                        return null;

                    var parent = ReadSync<ThingWithComps>(data);
                    if (parent == null)
                        return null;

                    var compType = thingCompTypes[compTypeId];
                    return parent.AllComps.Find(comp => comp.props.compClass == compType);
                }
                else if (typeof(WorkGiver).IsAssignableFrom(type))
                {
                    var def = ReadSync<WorkGiverDef>(data);
                    return def?.Worker;
                }
                else if (typeof(BillStack) == type)
                {
                    var thing = ReadSync<Thing>(data);
                    if (thing is IBillGiver billGiver)
                        return billGiver.BillStack;
                    return null;
                }
                else if (typeof(Bill).IsAssignableFrom(type))
                {
                    var billStack = ReadSync<BillStack>(data);
                    if (billStack == null)
                        return null;

                    var id = data.ReadInt32();
                    return billStack.Bills.Find(bill => bill.loadID == id);
                }
                else if (typeof(Outfit) == type)
                {
                    var id = data.ReadInt32();
                    return Current.Game.outfitDatabase.AllOutfits.Find(o => o.uniqueId == id);
                }
                else if (typeof(DrugPolicy) == type)
                {
                    var id = data.ReadInt32();
                    return Current.Game.drugPolicyDatabase.AllPolicies.Find(o => o.uniqueId == id);
                }
                else if (typeof(FoodRestriction) == type)
                {
                    var id = data.ReadInt32();
                    return Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Find(o => o.id == id);
                }
                else if (typeof(BodyPartRecord) == type)
                {
                    var partIndex = data.ReadUShort();
                    if (partIndex == ushort.MaxValue) return null;

                    var body = ReadSync<BodyDef>(data);
                    return body.GetPartAtIndex(partIndex);
                }
                else if (typeof(TransferableImmutable) == type)
                {
                    var things = ReadSync<List<Thing>>(data);

                    var tr = new TransferableImmutable();
                    tr.things.AddRange(things.NotNull());

                    return tr;
                }
                else if (typeof(MpTransferableReference) == type)
                {
                    var sessionId = data.ReadInt32();
                    var session = GetSessions(map).FirstOrDefault(s => s.SessionId == sessionId);
                    if (session == null) return null;

                    var thingId = data.ReadInt32();
                    if (thingId == -1) return null;

                    var transferable = session.GetTransferableByThingId(thingId);
                    if (transferable == null) return null;

                    return new MpTransferableReference(session, transferable);
                }
                else if (typeof(Lord) == type)
                {
                    var lordId = data.ReadInt32();
                    return map.lordManager.lords.Find(l => l.loadID == lordId);
                }
                else if (typeof(ISelectable) == type)
                {
                    var impl = ReadSync<ISelectableImpl>(data);

                    if (impl == ISelectableImpl.Thing)
                        return ReadSync<Thing>(data);
                    else if (impl == ISelectableImpl.Zone)
                        return ReadSync<Zone>(data);
                    else if (impl == ISelectableImpl.WorldObject)
                        return ReadSync<WorldObject>(data);
                    else
                        throw new Exception($"Unknown ISelectable");
                }
                else if (typeof(IStoreSettingsParent) == type)
                {
                    return ReadWithImpl<IStoreSettingsParent>(data, storageParents);
                }
                else if (typeof(IPlantToGrowSettable) == type)
                {
                    return ReadWithImpl<IPlantToGrowSettable>(data, plantToGrowSettables);
                }
                else if (typeof(IThingHolder) == type)
                {
                    return ReadWithImpl<IThingHolder>(data, supportedThingHolders);
                }
                else if (typeof(StorageSettings) == type)
                {
                    var parent = ReadSync<IStoreSettingsParent>(data);
                    if (parent == null) return null;
                    return parent.GetStoreSettings();
                }

                throw new SerializationException("No reader for type " + type);
            }
            catch (Exception e)
            {
                MpLog.Error($"Error reading type: {type}, {e}");
                throw;
            }
        }

        public static object[] ReadSyncObjects(ByteReader data, IEnumerable<SyncType> spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static object[] ReadSyncObjects(ByteReader data, IEnumerable<Type> spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static void WriteSync<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public static void WriteSyncObject(ByteWriter data, object obj, SyncType syncType)
        {
            var context = data.MpContext();
            var type = syncType.type;

            var log = data as LoggingByteWriter;
            log?.LogEnter(type.FullName + ": " + (obj ?? "null"));

            if (obj != null && !type.IsAssignableFrom(obj.GetType()))
                throw new SerializationException(
                    $"Serializing with type {type} but got object of type {obj.GetType()}");

            try
            {
                if (typeof(object) == type)
                {
                }
                else if (type.IsByRef)
                {
                }
                else if (syncType.expose)
                {
                    if (!typeof(IExposable).IsAssignableFrom(type))
                        throw new SerializationException($"Type {type} can't be exposed because it isn't IExposable");

                    var exposable = obj as IExposable;
                    data.WritePrefixedBytes(ScribeUtil.WriteExposable(exposable));
                }
                else if (typeof(IntVec3) == type)
                {
                    var vec = (IntVec3) obj;

                    if (vec.y < 0)
                    {
                        data.WriteShort(-1);
                    }
                    else
                    {
                        data.WriteShort((short) vec.y);
                        data.WriteShort((short) vec.x);
                        data.WriteShort((short) vec.z);
                    }
                }
                else if (writers.TryGetValue(type, out var writer))
                {
                    writer(data, obj);
                }
                else if (type.IsEnum)
                {
                    var enumType = Enum.GetUnderlyingType(type);
                    writers[enumType](data, Convert.ChangeType(obj, enumType));
                }
                else if (type.IsArray && type.GetArrayRank() == 1)
                {
                    var elementType = type.GetElementType();
                    var arr = (Array) obj;

                    if (arr.Length > ushort.MaxValue)
                        throw new Exception(
                            $"Tried to serialize a {elementType}[] with too many ({arr.Length}) items.");

                    data.WriteUShort((ushort) arr.Length);
                    foreach (var e in arr)
                        WriteSyncObject(data, e, elementType);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var specialList = ListType.Normal;
                    var listType = type.GetGenericArguments()[0];

                    if (listType == typeof(Thing) && obj == Find.CurrentMap.listerThings.AllThings)
                    {
                        context.map = Find.CurrentMap;
                        specialList = ListType.MapAllThings;
                    }
                    else if (listType == typeof(Designation) &&
                             obj == Find.CurrentMap.designationManager.allDesignations)
                    {
                        context.map = Find.CurrentMap;
                        specialList = ListType.MapAllDesignations;
                    }

                    WriteSync(data, specialList);

                    if (specialList == ListType.Normal)
                    {
                        var list = (IList) obj;

                        if (list.Count > ushort.MaxValue)
                            throw new Exception($"Tried to serialize a {type} with too many ({list.Count}) items.");

                        data.WriteUShort((ushort) list.Count);
                        foreach (var e in list)
                            WriteSyncObject(data, e, listType);
                    }
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    var isNull = obj == null;
                    data.WriteBool(isNull);
                    if (isNull) return;

                    var hasValue = (bool) obj.GetPropertyOrField("HasValue");
                    data.WriteBool(hasValue);

                    var nullableType = type.GetGenericArguments()[0];
                    if (hasValue)
                        WriteSyncObject(data, obj.GetPropertyOrField("Value"), nullableType);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var e = (IEnumerable) obj;
                    var elementType = type.GetGenericArguments()[0];
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = (IList) Activator.CreateInstance(listType);

                    foreach (var o in e)
                    {
                        if (list.Count > ushort.MaxValue)
                            throw new Exception($"Tried to serialize a {type} with too many ({list.Count}) items.");
                        list.Add(o);
                    }

                    WriteSyncObject(data, list, listType);
                }
                else if (typeof(Area).IsAssignableFrom(type))
                {
                    if (obj is Area area)
                    {
                        context.map = area.Map;
                        data.WriteInt32(area.ID);
                    }
                    else
                    {
                        data.WriteInt32(-1);
                    }
                }
                else if (typeof(Zone).IsAssignableFrom(type))
                {
                    if (obj is Zone zone)
                    {
                        context.map = zone.Map;
                        data.WriteInt32(zone.ID);
                    }
                    else
                    {
                        data.WriteInt32(-1);
                    }
                }
                else if (typeof(Def).IsAssignableFrom(type))
                {
                    var def = obj as Def;
                    data.WriteUShort(def != null ? def.shortHash : (ushort) 0);
                }
                else if (typeof(WITab_Caravan_Gear) == type)
                {
                    var tab = (WITab_Caravan_Gear) obj;
                    data.WriteBool(tab.draggedItem != null);
                    if (tab.draggedItem != null)
                        WriteSync(data, tab.draggedItem);
                }
                else if (typeof(PawnColumnWorker).IsAssignableFrom(type))
                {
                    var worker = obj as PawnColumnWorker;
                    WriteSync(data, worker.def);
                }
                else if (typeof(Command_SetPlantToGrow) == type)
                {
                    var command = (Command_SetPlantToGrow) obj;
                    WriteSync(data, command.settable);
                    WriteSync(data, command.settables);
                }
                else if (typeof(Command_SetTargetFuelLevel) == type)
                {
                    var command = (Command_SetTargetFuelLevel) obj;
                    WriteSync(data, command.refuelables);
                }
                else if (typeof(Command_LoadToTransporter) == type)
                {
                    var command = (Command_LoadToTransporter) obj;
                    WriteSync(data, command.transComp);
                    WriteSync(data, command.transporters ?? new List<CompTransporter>());
                }
                else if (typeof(Designator).IsAssignableFrom(type))
                {
                    var des = obj as Designator;
                    data.WriteUShort((ushort) Array.IndexOf(designatorTypes, des.GetType()));

                    if (des is Designator_Build build)
                        WriteSync(data, build.PlacingDef);
                }
                else if (typeof(CompChangeableProjectile) == type) // special case of ThingComp
                {
                    var comp = obj as CompChangeableProjectile;
                    if (comp == null)
                    {
                        WriteSync<Thing>(data, null);
                        return;
                    }

                    var compEquippable = comp.parent.TryGetComp<CompEquippable>();

                    if (compEquippable.AllVerbs.Any())
                    {
                        var turretGun = compEquippable.AllVerbs.Select(v => v.caster).OfType<Building_TurretGun>()
                            .FirstOrDefault();
                        if (turretGun != null)
                        {
                            WriteSync<Thing>(data, turretGun);
                            return;
                        }
                    }

                    throw new SerializationException("Couldn't save CompChangeableProjectile for thing " + comp.parent);
                }
                else if (typeof(ThingComp).IsAssignableFrom(type))
                {
                    var comp = (ThingComp) obj;
                    if (comp != null)
                    {
                        data.WriteUShort((ushort) Array.IndexOf(thingCompTypes, comp.GetType()));
                        WriteSync(data, comp.parent);
                    }
                    else
                    {
                        data.WriteUShort(ushort.MaxValue);
                    }
                }
                else if (typeof(WorkGiver).IsAssignableFrom(type))
                {
                    var workGiver = obj as WorkGiver;
                    WriteSync(data, workGiver.def);
                }
                else if (typeof(Thing).IsAssignableFrom(type))
                {
                    var thing = obj as Thing;
                    if (thing == null)
                    {
                        data.WriteInt32(-1);
                        return;
                    }

                    if (thing.Spawned)
                        context.map = thing.Map;

                    data.WriteInt32(thing.thingIDNumber);

                    if (!context.syncingThingParent)
                    {
                        object holder = null;

                        if (thing.Spawned)
                            holder = thing.Map;
                        else if (thing.ParentHolder is ThingComp thingComp)
                            holder = thingComp;
                        else if (ThingOwnerUtility.GetFirstSpawnedParentThing(thing) is Thing parentThing)
                            holder = parentThing;
                        else if (GetAnyParent<WorldObject>(thing) is WorldObject worldObj)
                            holder = worldObj;
                        else if (GetAnyParent<WorldObjectComp>(thing) is WorldObjectComp worldObjComp)
                            holder = worldObjComp;

                        GetImpl(holder, supportedThingHolders, out var implType, out var index);
                        if (index == -1)
                        {
                            data.WriteByte(byte.MaxValue);
                            Log.Error($"Thing {ThingHolderString(thing)} is inaccessible");
                            return;
                        }

                        data.WriteByte((byte) index);

                        if (implType != typeof(Map))
                        {
                            context.syncingThingParent = true;
                            WriteSyncObject(data, holder, implType);
                            context.syncingThingParent = false;
                            return;
                        }
                    }
                }
                else if (typeof(WorldObject).IsAssignableFrom(type))
                {
                    var worldObj = (WorldObject) obj;
                    data.WriteInt32(worldObj?.ID ?? -1);
                }
                else if (typeof(WorldObjectComp).IsAssignableFrom(type))
                {
                    var comp = (WorldObjectComp) obj;
                    if (comp != null)
                    {
                        var index = (ushort) Array.IndexOf(worldObjectCompTypes, comp.GetType());
                        data.WriteUShort(index);
                        WriteSync(data, comp.parent);
                    }
                    else
                    {
                        data.WriteInt32(ushort.MaxValue);
                    }
                }
                else if (typeof(BillStack) == type)
                {
                    var billGiver = (obj as BillStack)?.billGiver as Thing;
                    WriteSync(data, billGiver);
                }
                else if (typeof(Bill).IsAssignableFrom(type))
                {
                    var bill = (Bill) obj;
                    WriteSync(data, bill.billStack);
                    data.WriteInt32(bill.loadID);
                }
                else if (typeof(Outfit) == type)
                {
                    var outfit = (Outfit) obj;
                    data.WriteInt32(outfit.uniqueId);
                }
                else if (typeof(DrugPolicy) == type)
                {
                    var outfit = (DrugPolicy) obj;
                    data.WriteInt32(outfit.uniqueId);
                }
                else if (typeof(FoodRestriction) == type)
                {
                    var foodRestriction = (FoodRestriction) obj;
                    data.WriteInt32(foodRestriction.id);
                }
                else if (typeof(BodyPartRecord) == type)
                {
                    if (obj == null)
                    {
                        data.WriteUShort(ushort.MaxValue);
                        return;
                    }

                    var part = obj as BodyPartRecord;
                    var body = part.body;

                    data.WriteUShort((ushort) body.GetIndexOfPart(part));
                    WriteSync(data, body);
                }
                else if (typeof(TransferableImmutable) == type)
                {
                    // todo find a better way
                    var tr = (TransferableImmutable) obj;
                    WriteSync(data, tr.things);
                }
                else if (typeof(MpTransferableReference) == type)
                {
                    var reference = (MpTransferableReference) obj;
                    data.WriteInt32(reference.session.SessionId);

                    var tr = reference.transferable;

                    Thing thing;
                    if (tr is Tradeable trad)
                        thing = trad.FirstThingTrader ?? trad.FirstThingColony;
                    else if (tr is TransferableOneWay oneWay)
                        thing = oneWay.AnyThing;
                    else
                        throw new Exception($"Syncing unsupported transferable type {obj?.GetType()}");

                    if (thing.Spawned)
                        context.map = thing.Map;

                    data.WriteInt32(thing.thingIDNumber);
                }
                else if (typeof(Lord) == type)
                {
                    var lord = (Lord) obj;
                    context.map = lord.Map;
                    data.WriteInt32(lord.loadID);
                }
                else if (typeof(ISelectable) == type)
                {
                    if (obj is Thing thing)
                    {
                        WriteSync(data, ISelectableImpl.Thing);
                        WriteSync(data, thing);
                    }
                    else if (obj is Zone zone)
                    {
                        WriteSync(data, ISelectableImpl.Zone);
                        WriteSync(data, zone);
                    }
                    else if (obj is WorldObject worldObj)
                    {
                        WriteSync(data, ISelectableImpl.WorldObject);
                        WriteSync(data, worldObj);
                    }
                    else
                    {
                        throw new SerializationException($"Unknown ISelectable type: {obj?.GetType()}");
                    }
                }
                else if (typeof(IStoreSettingsParent) == type)
                {
                    WriteWithImpl<IStoreSettingsParent>(data, obj, storageParents);
                }
                else if (typeof(IPlantToGrowSettable) == type)
                {
                    WriteWithImpl<IPlantToGrowSettable>(data, obj, plantToGrowSettables);
                }
                else if (typeof(IThingHolder) == type)
                {
                    WriteWithImpl<IThingHolder>(data, obj, supportedThingHolders);
                }
                else if (typeof(StorageSettings) == type)
                {
                    var storage = obj as StorageSettings;
                    WriteSync(data, storage.owner);
                }
                else
                {
                    log?.LogNode("No writer for " + type);
                    throw new SerializationException("No writer for type " + type);
                }
            }
            catch (Exception e)
            {
                MpLog.Error($"Error writing type: {type}, obj: {obj}, {e}");
                throw;
            }
            finally
            {
                log?.LogExit();
            }
        }

        private static T ReadWithImpl<T>(ByteReader data, IList<Type> impls) where T : class
        {
            var impl = data.ReadUShort();
            if (impl == ushort.MaxValue) return null;
            return (T) ReadSyncObject(data, impls[impl]);
        }

        private static void WriteWithImpl<T>(ByteWriter data, object obj, IList<Type> impls) where T : class
        {
            if (obj == null)
            {
                data.WriteUShort(ushort.MaxValue);
                return;
            }

            GetImpl(obj, impls, out var implType, out var impl);

            if (implType == null)
                throw new SerializationException($"Unknown {typeof(T)} implementation type {obj.GetType()}");

            data.WriteUShort((ushort) impl);
            WriteSyncObject(data, obj, implType);
        }

        private static void GetImpl(object obj, IList<Type> impls, out Type type, out int index)
        {
            type = null;
            index = -1;

            if (obj == null) return;

            for (var i = 0; i < impls.Count; i++)
                if (impls[i].IsAssignableFrom(obj.GetType()))
                {
                    type = impls[i];
                    index = i;
                    break;
                }
        }

        private static T GetAnyParent<T>(Thing thing) where T : class
        {
            var t = thing as T;
            if (t != null)
                return t;

            for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
                if (parentHolder is T t2)
                    return t2;

            return (T) (object) null;
        }

        private static string ThingHolderString(Thing thing)
        {
            var builder = new StringBuilder(thing.ToString());

            for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
            {
                builder.Insert(0, "=>");
                builder.Insert(0, parentHolder.ToString());
            }

            return builder.ToString();
        }

        private static IEnumerable<ISessionWithTransferables> GetSessions(Map map)
        {
            foreach (var s in Multiplayer.WorldComp.trading)
                yield return s;

            if (map == null) yield break;
            var mapComp = map.MpComp();

            if (mapComp.caravanForming != null)
                yield return mapComp.caravanForming;

            if (mapComp.transporterLoading != null)
                yield return mapComp.transporterLoading;
        }

        private enum ListType : byte
        {
            Normal,
            MapAllThings,
            MapAllDesignations
        }

        private enum ISelectableImpl : byte
        {
            Thing,
            Zone,
            WorldObject
        }
    }

    internal class ReaderDictionary : OrderedDict<Type, Func<ByteReader, object>>
    {
        public void Add<T>(Func<ByteReader, T> writer)
        {
            Add(typeof(T), data => writer(data));
        }
    }

    internal class WriterDictionary : OrderedDict<Type, Action<ByteWriter, object>>
    {
        public void Add<T>(Action<ByteWriter, T> writer)
        {
            Add(typeof(T), (data, o) => writer(data, (T) o));
        }
    }

    public class SerializationException : Exception
    {
        public SerializationException(string msg) : base(msg)
        {
        }
    }
}