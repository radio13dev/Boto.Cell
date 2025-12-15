using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;
using Random = Unity.Mathematics.Random;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine.InputSystem;


public partial struct Game : ISystem
{
    const int VirusCount = 10;
    const int CellCount = 10;
    static readonly int2 MapSize = int2(50, 50);

    EntityArchetype m_ArchetypeVirus;
    EntityArchetype m_ArchetypeCell;

    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<ShopData>();

        m_ArchetypeVirus = state.EntityManager.CreateArchetype(
            // Main
            typeof(Virus), typeof(Transform), typeof(Velocity), typeof(Collider),
            typeof(Transform.FaceMoveDirection),
            typeof(HasParentTag), typeof(Parent),
            typeof(Input), typeof(RtsCommandBuffer), typeof(RtsCommandBuffer.Memory),
            typeof(DNA.Source),
            // Animation
            typeof(Virus.AnimData)
        );
        m_ArchetypeCell = state.EntityManager.CreateArchetype(
            // Main
            typeof(Cell), typeof(Transform), typeof(Velocity), typeof(Collider),
            typeof(HasChildrenTag), typeof(ChildrenDirty), typeof(ParentTransform), typeof(Children),
            typeof(Input), typeof(RtsCommandBuffer), typeof(RtsCommandBuffer.Memory),
            // Animation
            typeof(Cell.AnimData)
        );

        Random r = Random.CreateFromIndex(0);
        for (int i = 0; i < VirusCount; i++)
        {
            Entity virusE = state.EntityManager.CreateEntity(m_ArchetypeVirus);
            if (i == 0) state.EntityManager.AddComponent<Player>(virusE);
            state.EntityManager.SetComponentEnabled<DNA.Source>(virusE, false);
            state.EntityManager.SetComponentEnabled<HasParentTag>(virusE, false);
            state.EntityManager.SetComponentData(virusE, new Collider() { Radius = 1, Type = Collider.eType.Circle });
            state.EntityManager.SetComponentData(virusE, Transform.FromPositionRotation(r.NextFloat2(MapSize), r.NextFloat()));
        }

        for (int i = 0; i < CellCount; i++)
        {
            var cellE = state.EntityManager.CreateEntity(m_ArchetypeCell);
            state.EntityManager.SetComponentData(cellE, new Collider() { Radius = 10, Type = Collider.eType.Circle });
            state.EntityManager.SetComponentData(cellE, Transform.FromPosition(r.NextFloat2(MapSize)));

            // TODO: Add walls to cell so they're kinda bouncy
            //for (byte j = 0; j < Cell.Wall.Count; j++)
            //{
            //    var cellWallE = state.EntityManager.CreateEntity(typeof(Cell.Wall), typeof(Position), typeof(Rotation), typeof(Velocity), typeof(Collider));
            //    state.EntityManager.SetComponentData(cellE, new Collider(){ Radius = 10, Type = Collider.eType.Circle });
            //    state.EntityManager.SetComponentData(cellE, new Position(){ Value = r.NextFloat2(MapSize) });
            //    state.EntityManager.SetComponentData(cellE, new Rotation(){ Value = r.NextFloat(math.PI2) });
            //}
        }
    }
}

public partial struct MovementSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new FaceDirectionJob()
        {
            dt = SystemAPI.Time.DeltaTime
        }.Schedule(state.Dependency);
        state.Dependency = new DontFaceDirectionJob()
        {
            dt = SystemAPI.Time.DeltaTime
        }.Schedule(state.Dependency);
        state.Dependency = new RecordParentTransformJob()
        {
        }.Schedule(state.Dependency);
        state.Dependency = new UpdateChildrenJob()
        {
            ParentTransformLookup = SystemAPI.GetComponentLookup<ParentTransform>(true)
        }.Schedule(state.Dependency);
    }

    [WithNone(typeof(HasParentTag))]
    [WithAll(typeof(Transform.FaceMoveDirection))]
    partial struct FaceDirectionJob : IJobEntity
    {
        [ReadOnly] public float dt;

        public void Execute(ref Transform position, ref Velocity velocity)
        {
            position.Position += velocity.Value * dt;
            position.Direction = math.normalizesafe(velocity.Value, position.Direction);
            velocity.Value += -velocity.Value * dt;
        }
    }

    [WithNone(typeof(HasParentTag))]
    [WithNone(typeof(Transform.FaceMoveDirection))]
    partial struct DontFaceDirectionJob : IJobEntity
    {
        [ReadOnly] public float dt;

        public void Execute(ref Transform position, ref Velocity velocity)
        {
            position.Position += velocity.Value * dt;
            velocity.Value += -velocity.Value * dt;
        }
    }

    partial struct RecordParentTransformJob : IJobEntity
    {
        public void Execute(in Transform transform, in Velocity velocity, ref ParentTransform parent)
        {
            parent.Transform = transform;
            parent.Velocity = velocity;
        }
    }

    [WithAll(typeof(HasParentTag))]
    partial struct UpdateChildrenJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<ParentTransform> ParentTransformLookup;

        public void Execute(in Parent parent, ref Transform transform, ref Velocity velocity)
        {
            if (parent.Entity == Entity.Null) return;

            var parentT = ParentTransformLookup[parent.Entity];
            transform = parentT.Transform.TransformTransform(parent.Offset);
            velocity = parentT.Velocity;
        }
    }
}

public partial struct InputSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        new Job()
        {
            time = SystemAPI.Time.ElapsedTime
        }.Schedule();
    }

    [WithNone(typeof(Virus))]
    partial struct Job : IJobEntity
    {
        [ReadOnly] public double time;

        public void Execute(ref Velocity velocity, ref Input input)
        {
            if (input.Action == 1)
            {
                if (time < input.DisableUntilTime)
                {
                    //input.DisableUntilTime -= 0.1f;
                }
                else
                {
                    velocity.Value += normalizesafe(input.Vec0);
                    input.DisableUntilTime = time + Input.Cooldown;
                }
            }

            input.Clear();
        }
    }
}

public struct ShopData : IComponentData
{
    public int DrillTier;
}

public partial struct DnaChangesProcessSystem : ISystem
{
    const bool AnimationsEnabled = true;

    EntityQuery m_DnaSourceQuery;
    EntityQuery m_PlayerTransformQuery;
    
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<DNA>();
        state.RequireForUpdate<DNA>();
        
        state.EntityManager.CreateSingleton<DNA.Changes>();
        SystemAPI.SetSingleton(new DNA.Changes(new NativeList<DNA.Change>(256, Allocator.Persistent)));
        state.RequireForUpdate<DNA.Changes>();
        
        state.EntityManager.CreateSingleton<DNA.AnimData>();
        SystemAPI.SetSingleton(DNA.AnimData.Default);
        state.RequireForUpdate<DNA.AnimData>();
        
        m_DnaSourceQuery = SystemAPI.QueryBuilder().WithAll<DNA.Source>().Build();
        m_PlayerTransformQuery = SystemAPI.QueryBuilder().WithAll<Player, Transform, Collider>().Build();
    }

    public void OnDestroy(ref SystemState state)
    {
        SystemAPI.GetSingleton<DNA.Changes>().Data.Dispose();
        SystemAPI.GetSingleton<DNA.AnimData>().Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Get shop data
        var shopData = SystemAPI.GetSingleton<ShopData>();
    
        // Increment
        var addCount = m_DnaSourceQuery.CalculateEntityCount();
    
        // Animations
        if (AnimationsEnabled)
        {
            ref var changes = ref SystemAPI.GetSingletonRW<DNA.Changes>().ValueRW;
            changes.Data.SetCapacity(changes.Data.Length + addCount);
            state.Dependency = new DnaSourceAnimationJob()
            {
                ShopData = shopData,
                DnaChanges = changes.Data.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            state.CompleteDependency();
            
            // Start doing animations
            ref var animations = ref SystemAPI.GetSingletonRW<DNA.AnimData>().ValueRW;
            // Add change effects
            for (int i = 0; i < changes.Data.Length; i++)
            {
                var change = changes.Data[i];
                animations.ApplyChange(change);
            }
            // Clear changes list
            changes.Data.Clear();
            
            // More animations
            if (m_PlayerTransformQuery.TryGetSingleton(out Transform playerT) && m_PlayerTransformQuery.TryGetSingleton(out Collider playerC))
            {
                animations.UpdateGroups(SystemAPI.Time.DeltaTime, playerT, playerC);
                animations.UpdateMergers(SystemAPI.Time.DeltaTime, playerT, playerC);
            }
        }
        
        SystemAPI.GetSingletonRW<DNA>().ValueRW.Value += (ulong)addCount*(ulong)shopData.DrillTier;
        
        state.Dependency = new DnaSourceConsumptionJob().ScheduleParallel(state.Dependency);
        state.CompleteDependency();
    }
    
    [WithAll(typeof(DNA.Source))]
    partial struct DnaSourceAnimationJob : IJobEntity
    {
        [ReadOnly] public ShopData ShopData;
        public NativeList<DNA.Change>.ParallelWriter DnaChanges;
        public void Execute(in Transform transform)
        {
            DnaChanges.AddNoResize(new DNA.Change(transform, ShopData.DrillTier+1));
        }
    }
    partial struct DnaSourceConsumptionJob : IJobEntity
    {
        public void Execute(EnabledRefRW<DNA.Source> sourceState)
        {
            sourceState.ValueRW = false;
        }
    }
}

public partial struct Virus_InputSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ShopData>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);
        state.Dependency = new Job()
        {
            ecb = ecb,
            time = SystemAPI.Time.ElapsedTime,
            dt = SystemAPI.Time.DeltaTime,
            ParentTransformLookup = SystemAPI.GetComponentLookup<ParentTransform>(true),
            ParentColliderLookup = SystemAPI.GetComponentLookup<Collider>(true),
            ShopPurchases = SystemAPI.GetSingleton<ShopData>(),
        }.Schedule(state.Dependency);
        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    [WithPresent(typeof(Parent))]
    [WithPresent(typeof(DNA.Source))]
    public partial struct Job : IJobEntity
    {
        public EntityCommandBuffer ecb;
        [ReadOnly] public double time;
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentLookup<ParentTransform> ParentTransformLookup;
        [ReadOnly] public ComponentLookup<Collider> ParentColliderLookup;
        [ReadOnly] public ShopData ShopPurchases;

        public unsafe void Execute(Entity entity, in Virus virus, in Virus.AnimData virusAnimData, ref Transform transform, ref Velocity velocity, in Collider collider,
            ref Input input, ref Parent oldParentData,
            EnabledRefRW<DNA.Source> dnaCollected)
        {
            if (input.Action == 1)
            {
                if (time < input.DisableUntilTime)
                {
                    //input.DisableUntilTime -= 0.1f;
                }
                else
                {
                    velocity.Value += normalizesafe(input.Vec0);
                    input.DisableUntilTime = time + Input.Cooldown;
                }
            }

            if (input.Action == 1)
            {
                if (input.ActionRef == oldParentData.Entity)
                {
                    if (input.ActionRef != Entity.Null)
                    {
                        dnaCollected.ValueRW = true;
                    }
                }
                else
                {
                    bool validTarget = ParentTransformLookup.TryGetComponent(input.ActionRef, out var newParentT);
                    validTarget &= ParentColliderLookup.TryGetComponent(input.ActionRef, out var parentC);

                    // Detach
                    if (oldParentData.Entity != Entity.Null && ParentTransformLookup.HasComponent(oldParentData.Entity))
                        ecb.SetComponentEnabled<ChildrenDirty>(oldParentData.Entity, true);

                    if (validTarget && Collider.Overlaps(newParentT.Transform.Position, parentC, transform.Position, new Collider() { Radius = 3 }))
                    {
                        var newParentE = input.ActionRef;

                        // Snap us to the parent
                        // Push us out of the parent collider
                        Collider.MoveOutOf(transform.Position, collider, newParentT.Transform.Position, parentC, out float2 shift, out _);
                        transform.Position += shift;
                        transform.Direction = math.normalizesafe(newParentT.Transform.Position - transform.Position, float2(1, 0));

                        // Setup child-to-parent link
                        var newParentData = new Parent()
                        {
                            Entity = newParentE,
                            Offset = newParentT.Transform.InverseTransformTransform(transform)
                        };
                        ecb.SetComponentEnabled<HasParentTag>(entity, true);
                        ecb.SetComponent(entity, newParentData);

                        // Setup parent-to-child link
                        ecb.AppendToBuffer(newParentE, new Children() { Entity = entity, Offset = newParentData.Offset, Collider = collider });
                        ecb.SetComponentEnabled<ChildrenDirty>(newParentE, true);
                    }
                    else
                    {
                        // Clear data in self
                        oldParentData = default;
                        ecb.SetComponentEnabled<HasParentTag>(entity, false);
                    }
                }
            }

            input.Clear();
        }
    }
}

public partial struct CollisionSystem : ISystem
{
    EntityQuery m_Query;

    public void OnCreate(ref SystemState state)
    {
        m_Query = SystemAPI.QueryBuilder().WithAll<Collider, Transform>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        var entities = m_Query.ToEntityArray(Allocator.TempJob);
        var colliders = m_Query.ToComponentDataArray<Collider>(Allocator.TempJob);
        var positions = m_Query.ToComponentDataArray<Transform>(Allocator.TempJob);

        state.Dependency = new Job()
        {
            Entities = entities,
            Colliders = colliders,
            Positions = positions
        }.Schedule(state.Dependency);

        entities.Dispose(state.Dependency);
        colliders.Dispose(state.Dependency);
        positions.Dispose(state.Dependency);
    }

    partial struct Job : IJobEntity
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<Collider> Colliders;
        [ReadOnly] public NativeArray<Transform> Positions;

        public void Execute(Entity entity, ref Transform transform, ref Velocity velocity, in Collider collider, in DynamicBuffer<Children> children)
        {
            for (int i = 0; i < Colliders.Length; i++)
            {
                if (Entities[i] == entity) goto Skip;
                for (int j = 0; j < children.Length; j++)
                    if (Entities[i] == children[j].Entity)
                        goto Skip;

                for (int j = 0; j < children.Length; j++)
                {
                    // Forces applied to children are instead applied to us, think of them as an extended collider
                    _HandleCollision(transform.TransformPoint(children[j].Offset.Position), children[j].Collider, Positions[i].Position, Colliders[i], ref transform, ref velocity);
                }

                _HandleCollision(transform.Position, collider, Positions[i].Position, Colliders[i], ref transform, ref velocity);
                Skip: ;
            }
        }

        private void _HandleCollision(in float2 p0, in Collider c0, in float2 p1, in Collider c1, ref Transform transform, ref Velocity velocity)
        {
            if (!Collider.Overlaps(p0, c0, p1, c1)) return;

            Collider.MoveOutOf(p0, c0, p1, c1, out float2 childShift, out float2 childReflectNormal);
            transform.Position += childShift / 2;

            var childVelDot = math.dot(velocity.Value, childReflectNormal);
            if (childVelDot < 0)
                velocity.Value = math.reflect(velocity.Value, childReflectNormal);

            velocity.Value += childShift;
        }
    }
}

public struct Transform : IComponentData
{
    public float2 Position;
    public float2 Direction;
    public float2 Forward() => Direction;
    public float2 Right() => float2(-Direction.y, Direction.x);
    public float Angle() => atan2(Direction.y, Direction.x);

    public struct FaceMoveDirection : IComponentData
    {
    }

    public static Transform FromPosition(float2 position) => FromPositionRotation(position, 0);

    public static Transform FromPositionRotation(float2 position, float radians)
    {
        return new Transform()
        {
            Position = position,
            Direction = new float2(cos(radians), sin(radians))
        };
    }

    public float2 TransformPoint(float2 offsetPosition)
    {
        offsetPosition = math.mul(new float2x2(Direction, new float2(-Direction.y, Direction.x)), offsetPosition);
        return offsetPosition + Position;
    }

    public Transform TransformTransform(Transform transform)
    {
        var newPos = TransformPoint(transform.Position);
        var newDir = math.mul(new float2x2(Direction, new float2(-Direction.y, Direction.x)), transform.Direction);
        return new Transform()
        {
            Position = newPos,
            Direction = newDir
        };
    }

    public Transform InverseTransformTransform(Transform transform)
    {
        var offsetPos = transform.Position - Position;
        var newPos = math.mul(transpose(new float2x2(Direction, new float2(-Direction.y, Direction.x))), offsetPos);
        var newDir = math.mul(transpose(float2x2(Direction, new float2(-Direction.y, Direction.x))), transform.Direction);
        return new Transform()
        {
            Position = newPos,
            Direction = newDir
        };
    }

    public Transform Offset(float2 offset)
    {
        var t = this;
        t.Position += offset;
        return t;
    }

    public static Transform Lerp(Transform a, Transform b, float t)
    {
        return Transform.FromPositionRotation(math.lerp(a.Position, b.Position, t), mathu.lerpangle(a.Angle(), b.Angle(), t));
    }
}

public struct Velocity : IComponentData
{
    public float2 Value;
}

public struct HasChildrenTag : IComponentData
{
}

public struct ParentTransform : IComponentData, ICleanupComponentData // On cleanup: Notify children
{
    public Transform Transform;
    public Velocity Velocity;
}

public struct Children : IBufferElementData, ICleanupBufferElementData
{
    public Entity Entity;
    public Transform Offset; // Duplicated in Parent
    public Collider Collider;
}

public struct ChildrenDirty : IComponentData, IEnableableComponent
{
}

public struct HasParentTag : IComponentData, IEnableableComponent
{
}

public struct Parent : IComponentData, ICleanupComponentData // On cleanup: Notify parent
{
    public Entity Entity;
    public Transform Offset; // Duplicated in Children
}

public partial struct ParentCleanupSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        using var ecb = new EntityCommandBuffer(Allocator.TempJob);
        state.Dependency = new ParentCleanupJob()
        {
            ecb = ecb
        }.Schedule(state.Dependency);
        state.Dependency = new ChildCleanupJob()
        {
            ecb = ecb
        }.Schedule(state.Dependency);
        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);
        state.Dependency = new UpdateChildrenJob()
        {
            ParentLookup = SystemAPI.GetComponentLookup<Parent>(true)
        }.Schedule(state.Dependency);
    }

    [WithNone(typeof(HasChildrenTag))]
    partial struct ParentCleanupJob : IJobEntity
    {
        public EntityCommandBuffer ecb;

        public void Execute(Entity entity, in DynamicBuffer<Children> children)
        {
            for (int i = 0; i < children.Length; i++)
            {
                ecb.SetComponent(children[i].Entity, new Parent());
                ecb.SetComponentEnabled<HasParentTag>(children[i].Entity, false);
            }

            ecb.RemoveComponent<ParentTransform>(entity);
            ecb.RemoveComponent<Children>(entity);
        }
    }

    [WithAbsent(typeof(HasParentTag))]
    partial struct ChildCleanupJob : IJobEntity
    {
        public EntityCommandBuffer ecb;

        public void Execute(Entity entity, in Parent parent)
        {
            if (parent.Entity != Entity.Null) ecb.SetComponentEnabled<ChildrenDirty>(parent.Entity, true);
            ecb.RemoveComponent<Parent>(entity);
        }
    }

    partial struct UpdateChildrenJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<Parent> ParentLookup;

        public void Execute(Entity entity, EnabledRefRW<ChildrenDirty> childrenDirty, ref DynamicBuffer<Children> children)
        {
            childrenDirty.ValueRW = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (!ParentLookup.TryGetComponent(children[i].Entity, out var childParent) || childParent.Entity != entity)
                {
                    // Remove this child
                    children.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }
    }
}


public struct Square
{
    public float2 Center;
    public float Radius;

    public bool Overlaps(Square other)
    {
        // Return true if two squares overlap
        return math.any(math.abs(other.Center - Center) < Radius + other.Radius);
    }
}

public struct Collider : IComponentData
{
    public float Radius;
    public eType Type;

    public enum eType
    {
        Circle
    }

    public static bool Overlaps(in float2 p0, in Collider c0, in float2 p1, in Collider c1)
    {
        if (!new Square() { Center = p0, Radius = c0.Radius }.Overlaps(new Square() { Center = p1, Radius = c1.Radius })) return false;

        return math.distancesq(p0, p1) < math.square(c0.Radius + c1.Radius);
    }

    public static void MoveOutOf(in float2 p0, in Collider c0, in float2 p1, in Collider c1, out float2 shift, out float2 reflectNormal)
    {
        var dif = math.normalizesafe(p0 - p1, math.float2(1, 0));
        shift = p1 + (c0.Radius + c1.Radius) * dif * 1.01f - p0;
        reflectNormal = dif;
    }
}

public struct Player : IComponentData
{
}

public struct Input : IComponentData
{
    public float2 Vec0;

    public double DisableUntilTime;

    public byte Action;
    public Entity ActionRef;
    public const double Cooldown = 0.2f;

    public void Clear()
    {
        Vec0 = default;
        Action = default;
        ActionRef = default;
    }
}


public struct Virus : IComponentData
{
    public struct AnimData : IComponentData
    {
        public float _;
    }
}

// Currency for shop

public struct AttachToEntity : IComponentData, IEnableableComponent
{
    public Entity Entity;
}

public struct Cell : IComponentData
{
    public struct Wall : IComponentData
    {
        public const byte Count = 8;
        public byte Index;
        public Entity Parent;
    }

    public struct AnimData : IComponentData
    {
        public float _;
    }
}