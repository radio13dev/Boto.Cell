using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

public struct DNA : IComponentData
{
    public ulong Value;

    /// <summary>
    /// Enable state is 'consumed' to add 1 to the DNA counter
    /// </summary>
    public readonly struct Source : IComponentData, IEnableableComponent{}

    // List of changes to be applied to DNA value
    public struct Changes : IComponentData
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeList<Change> Data;
        
        public Changes(NativeList<Change> changes)
        {
            Data = changes;
        }
    }

    // All DNA changes are applied through these (adding or removing)
    public readonly struct Change
    {
        public readonly Transform Transform;
        public readonly int Value;

        public Change(Transform transform, int v)
        {
            this.Transform = transform;
            this.Value = v;
        }
    }

    public struct AnimData : IComponentData, IDisposable
    {
        public static AnimData Default
        {
            get
            {
                var data = 
                    new AnimData
                    {
                        Groups  = new NativeArray<Transform>(256, Allocator.Persistent),
                        Particles = new ParticleBitFlags(),
                        Mergers = new NativeList<Merger>(256, Allocator.Persistent)
                    };
                    for (int i = 0; i < data.Groups.Length; i++)
                    {
                        data.Groups[i] = Transform.FromPosition(0);
                    }
                    return data;
            }
        }

        
        public const float DNAChangeFadeSpeedLinear = 2;
        public const float DNAChangeFadeSpeedRelative = 10;
        public float TimeSinceDNAChange;
        public float DNATextScale => 1 + TimeSinceDNAChange;
            
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<Transform> Groups;
        [NativeDisableContainerSafetyRestriction]
        public ParticleBitFlags Particles;
        [NativeDisableContainerSafetyRestriction]
        public NativeList<Merger> Mergers;

        public void Dispose()
        {
            Groups.Dispose();
            Mergers.Dispose();
        }
        
        public unsafe struct ParticleBitFlags
        {
            fixed long Groups_0to256[16];
            
            public bool this[int index]
            {
                get
                {
                    // Find the long containing this (each long contains 16 groups)
                    var groupLong = index >> 4;
                    var bitPosition = index&63;
                    var val = Groups_0to256[groupLong];
                    return (val & (1L << bitPosition)) > 0;
                }
                set
                {
                    // Find the long containing this (each long contains 16 groups)
                    var groupLong = index >> 4;
                    var bitPosition = index%64;
                    var val = Groups_0to256[groupLong];
                    val = (val & ~(1L << bitPosition)) | ((value ? 1L : 0L) << bitPosition);
                    Groups_0to256[groupLong] = val;
                }
            }
        }


        public unsafe struct Merger
        {
            public Transform UnitParticle;
            
            public byte GroupIndex;
            public byte InnerGroupIndex;
            public int TargetParticleIndex => (GroupIndex<<2)+InnerGroupIndex;
            
            public int ChangeCount;
            public float T;
            public const float Lifespan = 0.2f;
        
            public static Merger NewParticle(Transform transform, int changeCount)
            {
                return new Merger()
                {
                    UnitParticle = transform,
                    ChangeCount = changeCount
                };
            }
            
            public static Merger RemoveParticle(Transform transform, int changeCount)
            {
                return new Merger()
                {
                    UnitParticle = transform,
                    ChangeCount = changeCount
                };
            }

            public static Merger MoveParticle(Transform transform, byte groupIndex, byte offset, bool write)
            {
                return new Merger()
                {
                    UnitParticle = transform,
                    ChangeCount = write ? 1 : 0,
                    GroupIndex = groupIndex,
                    InnerGroupIndex = offset
                };
            }
        }

        public void ApplyChange(Change change)
        {
            if (change.Value > 0)
            {
                Mergers.Add(Merger.NewParticle(change.Transform, change.Value));
            }
            else if (change.Value < 0)
            {
                Mergers.Add(Merger.RemoveParticle(change.Transform, -change.Value));
            }
        }
        
        public void UpdateGroups(float dt, Transform transform, Collider collider)
        {
            var lastT = transform;
            var lastR = collider.Radius / 2;
            for (int i = 0; i < Groups.Length; i++)
            {
                // First one must be at least x units from player
                // Next one must be x units from the last
                // Etc...
                var t = Groups[i];
                var r = DNA.AnimData.GetDnaGroupScale(collider);
                Collider.MoveOutOf(t.Position, new Collider() { Radius = r }, lastT.Position, new Collider() { Radius = lastR }, out float2 shift, out _);
                
                var group = Groups[i];
                group.Position += math.lerp(0, shift, math.clamp(dt * 10, 0, 1));
                Groups[i] = group;
                
                lastT = t;
                lastR = r;
            }
        }
        
        public void UpdateMergers(float dt, Transform transform, Collider collider)
        {
            for (int i = 0; i < Mergers.Length; i++)
            {
                ref var merger = ref Mergers.ElementAt(i);
                merger.T += dt;
                merger.T = math.clamp(merger.T, 0, Merger.Lifespan);
                
                var zero = Groups[merger.GroupIndex];
                var dnaSpacing = DNA.AnimData.GetDnaSpacing(collider);
                merger.UnitParticle = Transform.Lerp(merger.UnitParticle, zero.Offset(GetOffset(zero, merger.InnerGroupIndex)*dnaSpacing), merger.T/Merger.Lifespan);
                
                if (merger.T >= Merger.Lifespan)
                {
                    if (merger.ChangeCount > 0)
                    {
                        if (merger.InnerGroupIndex == 3)
                        {
                            // We completed a set
                            Mergers.Add(Merger.MoveParticle(merger.UnitParticle, (byte)(merger.GroupIndex+1), 0, true));
                            Mergers.Add(Merger.MoveParticle(zero.Offset(GetOffset(zero, 0)*dnaSpacing), (byte)(merger.GroupIndex+1), 0, false));
                            Mergers.Add(Merger.MoveParticle(zero.Offset(GetOffset(zero, 1)*dnaSpacing), (byte)(merger.GroupIndex+1), 0, false));
                            Mergers.Add(Merger.MoveParticle(zero.Offset(GetOffset(zero, 2)*dnaSpacing), (byte)(merger.GroupIndex+1), 0, false));
                            //Particles[merger.TargetParticleIndex] = false;
                            Particles[merger.TargetParticleIndex-1] = false;
                            Particles[merger.TargetParticleIndex-2] = false;
                            Particles[merger.TargetParticleIndex-3] = false;
                        }
                        else
                        if (!Particles[merger.TargetParticleIndex])
                        {
                            Particles[merger.TargetParticleIndex] = true;
                        }
                        else
                        {
                            // Push this particle to the next index
                            Mergers.Add(Merger.MoveParticle(merger.UnitParticle, (byte)(merger.GroupIndex), (byte)(merger.InnerGroupIndex+1), true));
                        }
                    }
                    Mergers.RemoveAt(i);
                    i--;
                }
            }
        }

        public static float2 GetOffset(Transform t, byte index)
        {
            if (index == 0) return t.Forward() + t.Right();
            else if (index == 1) return t.Forward() - t.Right();
            else if (index == 2) return -t.Forward() + t.Right();
            else return -t.Forward() - t.Right();
        }

        public static float GetDnaGroupScale(Collider c) => c.Radius/2;
        public static float GetDnaScale(Collider c) => c.Radius/5;
        public static float GetDnaSpacing(Collider c) => c.Radius/10;
    }
}