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

    public void OnCreate(ref SystemState state)
    {
        Random r = Random.CreateFromIndex(0);
        for (int i = 0; i < VirusCount; i++)
        {
            Entity virusE;
            if (i == 0)
                virusE = state.EntityManager.CreateEntity(
                    // Main
                    typeof(Virus), typeof(Transform), typeof(Velocity), typeof(Collider), typeof(HasParentTag), typeof(Parent), typeof(Input), typeof(Player),
                    // Animation
                    typeof(Virus.AnimData), typeof(DNA.Group), typeof(DNA.Particle), typeof(DNA.Merger)
                );
            else
                virusE = state.EntityManager.CreateEntity(typeof(Virus), typeof(Virus.AnimData), typeof(Transform), typeof(Velocity), typeof(Collider), typeof(HasParentTag),
                    typeof(Parent), typeof(Input));
            state.EntityManager.SetComponentEnabled<HasParentTag>(virusE, false);
            state.EntityManager.SetComponentData(virusE, new Collider() { Radius = 1, Type = Collider.eType.Circle });
            state.EntityManager.SetComponentData(virusE, Transform.FromPositionRotation(r.NextFloat2(MapSize), r.NextFloat()));
        }

        for (int i = 0; i < CellCount; i++)
        {
            var cellE = state.EntityManager.CreateEntity(typeof(Cell), typeof(Cell.AnimData), typeof(Transform), typeof(Velocity), typeof(Collider), typeof(HasChildrenTag),
                typeof(ChildrenDirty), typeof(ParentTransform), typeof(Children), typeof(Input));
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
        state.Dependency = new Job()
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
    partial struct Job : IJobEntity
    {
        [ReadOnly] public float dt;

        public void Execute(ref Transform position, ref Velocity velocity)
        {
            position.Position += velocity.Value * dt;
            position.Direction = math.normalizesafe(velocity.Value, position.Direction);
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
                    input.DisableUntilTime -= 0.1f;
                }
                else
                {
                    velocity.Value += normalizesafe(input.Vec0);
                    input.DisableUntilTime = time + 1f;
                }
            }

            input = default;
        }
    }
}

public partial struct Virus_InputSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.TempJob);
        state.Dependency = new Job()
        {
            ecb = ecb,
            time = SystemAPI.Time.ElapsedTime,
            dt = SystemAPI.Time.DeltaTime,
            ParentTransformLookup = SystemAPI.GetComponentLookup<ParentTransform>(true),
            ParentColliderLookup = SystemAPI.GetComponentLookup<Collider>(true)
        }.Schedule(state.Dependency);
        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    [WithPresent(typeof(Parent))]
    partial struct Job : IJobEntity
    {
        public EntityCommandBuffer ecb;
        [ReadOnly] public double time;
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentLookup<ParentTransform> ParentTransformLookup;
        [ReadOnly] public ComponentLookup<Collider> ParentColliderLookup;

        public unsafe void Execute(Entity entity, ref Virus virus, ref Virus.AnimData virusAnimData, ref Transform transform, ref Velocity velocity, in Collider collider,
            ref Input input, ref Parent oldParentData,
            ref DynamicBuffer<DNA.Group> groups, ref DynamicBuffer<DNA.Particle> particles, ref DynamicBuffer<DNA.Merger> mergers)
        {
            if (input.Action == 1)
            {
                if (time < input.DisableUntilTime)
                {
                    input.DisableUntilTime -= 0.1f;
                }
                else
                {
                    velocity.Value += normalizesafe(input.Vec0);
                    input.DisableUntilTime = time + 1f;
                }
            }

            if (input.Action == 1)
            {
                if (input.ActionRef == oldParentData.Entity)
                {
                    if (input.ActionRef != Entity.Null)
                    {
                        // Drain!
                        virus.DNA.Value++;

                        // Animation stuff:
                        virusAnimData.TimeSinceDNAChange++;
                        // Update groups
                        int len = math.max((1 + 64 - math.lzcnt(virus.DNA.Value))/2, 1);
                        while (groups.Length < len)
                            groups.Add(new DNA.Group(){ Transform = groups.Length > 0 ? groups[^1].Transform : transform });
                        while (particles.Length < 256)
                            particles.Add(new());
                        // Add particle anim stuff
                        byte indexInLowestGroup = (byte)((virus.DNA.Value - 1) & 3);
                        AddNewDnaParticle(ref particles, indexInLowestGroup, transform); // 0, 1, 2, or 3
                        if (indexInLowestGroup == 3) // This occurs each time we add the 4th item
                        {
                            // Merge lowest 4 particles
                            MergeParticles(ref mergers,
                                valueToApproach: virus.DNA.Value,
                                depthToMergeFrom: 0,
                                groups[0].Transform, collider.Radius);
                            DeleteParticles(ref particles, 0, 4);
                        }
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
                        transform.Direction = math.normalizesafe(newParentT.Transform.Position - transform.Position);

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

            input = default;


            // ...

            // Update group positions
            var lastT = transform;
            var lastR = collider.Radius/2;
            for (int i = 0; i < groups.Length; i++)
            {
                // First one must be at least x units from player
                // Next one must be x units from the last
                // Etc...
                var t = groups[i].Transform;
                var r = collider.Radius / 4;
                Collider.MoveOutOf(t.Position, new Collider() { Radius = r }, lastT.Position, new Collider() { Radius = lastR }, out float2 shift, out _);
                groups.ElementAt(i).Transform.Position += math.lerp(0, shift, math.clamp(dt * 10, 0, 1));
                lastT = t;
                lastR = r;
            }

            // ...

            for (int i = 0; i < mergers.Length; i++)
            {
                ref var merger = ref mergers.ElementAt(i);
                var targetGroup = groups[merger.DepthToMergeFrom + 1];
                merger.Update(dt, targetGroup.Transform, collider.Radius);

                if (merger.IsComplete)
                {
                    // Particles completed merge, create a 4th one and delete mergers
                    var zeroIndex = (1 + merger.DepthToMergeFrom) << 2;
                    var filledAtMerge = (byte)(((merger.ValueToApproach - 1) >> (2 + 2 * merger.DepthToMergeFrom)) & 3);
                    AddNewDnaParticle(ref particles, zeroIndex + filledAtMerge, targetGroup.Transform);

                    // Check if we've closed this group
                    if (filledAtMerge == 3)
                    {
                        // Merge this group
                        MergeParticles(ref mergers,
                            valueToApproach: merger.ValueToApproach,
                            depthToMergeFrom: merger.DepthToMergeFrom + 1,
                            targetGroup.Transform, collider.Radius);
                        DeleteParticles(ref particles, zeroIndex, 4);
                    }

                    mergers.RemoveAt(i);
                    i--;
                }
            }
        }

        private void AddNewDnaParticle(ref DynamicBuffer<DNA.Particle> particles, int index, Transform transform)
        {
            ref var particle = ref particles.ElementAt(index);
            particle.Active = true;
        }

        private void DeleteParticles(ref DynamicBuffer<DNA.Particle> particles, int offset, int length)
        {
            for (int i = 0; i < length; i++)
                particles.ElementAt(i + offset).Active = false;
        }

        private static void MergeParticles(ref DynamicBuffer<DNA.Merger> mergers, ulong valueToApproach, int depthToMergeFrom,
            Transform zero, float scale)
        {
            mergers.Add(new DNA.Merger(
                valueToApproach,
                depthToMergeFrom,
                zero,
                scale
            ));
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
            transform.Direction = math.normalizesafe(velocity.Value);

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
    public float2 Right => float2(-Direction.y, Direction.x); 

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
}

public struct Virus : IComponentData
{
    public DNA DNA;

    public struct AnimData : IComponentData
    {
        public const float DNAChangeFadeSpeedLinear = 2;
        public const float DNAChangeFadeSpeedRelative = 10;
        public float TimeSinceDNAChange;
        public float DNATextScale => 1 + TimeSinceDNAChange;
    }
}

public struct DNA
{
    public ulong Value;
    
    public struct Group : IBufferElementData
    {
        public Transform Transform;
    }

    public struct Particle : IBufferElementData
    {
        public bool Active;
    }

    public unsafe struct Merger : IBufferElementData
    {
        public float2 ParticleA;
        public float2 ParticleB;
        public float2 ParticleC;
        public float2 ParticleD;
        public readonly ulong ValueToApproach;
        public readonly int DepthToMergeFrom;
        
        public float T;
        public bool IsComplete => T >= 1;

        public Merger(ulong valueToApproach, int depthToMergeFrom, Transform zero, float scale)
        {
            ValueToApproach = valueToApproach;
            DepthToMergeFrom = depthToMergeFrom;
            
            float innerSpacing = scale/8;
            ParticleA = zero.Position + zero.Direction*innerSpacing + zero.Right*innerSpacing;
            ParticleB = zero.Position + zero.Direction*innerSpacing - zero.Right*innerSpacing;
            ParticleC = zero.Position - zero.Direction*innerSpacing + zero.Right*innerSpacing;
            ParticleD = zero.Position - zero.Direction*innerSpacing - zero.Right*innerSpacing;
            
            T = 0;
        }

        public void Update(float dt, Transform zero, float scale)
        {
            T += dt*5;
            
            float innerSpacing = scale/8;
            var bits = (byte)(((ValueToApproach - 1) >> (2 + 2 * DepthToMergeFrom)) & 3);
            if (bits == 0) zero.Position += zero.Direction*innerSpacing + zero.Right*innerSpacing;
            if (bits == 1) zero.Position += zero.Direction*innerSpacing - zero.Right*innerSpacing;
            if (bits == 2) zero.Position += -zero.Direction*innerSpacing + zero.Right*innerSpacing;
            if (bits == 3) zero.Position += -zero.Direction*innerSpacing - zero.Right*innerSpacing;
            
            ParticleA = math.lerp(ParticleA, zero.Position, T);
            ParticleB = math.lerp(ParticleB, zero.Position, T);
            ParticleC = math.lerp(ParticleC, zero.Position, T);
            ParticleD = math.lerp(ParticleD, zero.Position, T);
        }
        
        public static Transform lerp(Transform a, Transform b, float t)
        {
            var aAng = math.atan2(a.Direction.y,a.Direction.x);
            var bAng = math.atan2(b.Direction.y,b.Direction.x);
            return Transform.FromPositionRotation(math.lerp(a.Position,b.Position,t), LerpAngle(aAng,bAng,t));
        }
        public static float LerpAngle(float a, float b, float t)
        {
            float num = Repeat(b - a, math.PI2);
            if ((double) num > 180.0)
                num -= 360f;
            return a + num * math.clamp(t,0,1);
        }
        public static float Repeat(float t, float length)
        {
            return math.clamp(t - math.floor(t / length) * length, 0.0f, length);
        }
    }
}

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
    }
}