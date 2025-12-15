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

    // Represents the position of DNA in the tail
    public struct Position
    {
        public byte GroupIndex;
        public byte InnerGroupIndex;

        public Position(byte groupIndex, byte innerGroupIndex)
        {
            GroupIndex = groupIndex;
            InnerGroupIndex = innerGroupIndex;
        }

        public int TargetParticleIndex => (GroupIndex<<2)+InnerGroupIndex;
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
                        Particles = new ParticleBitFlags(256*4),
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
            Particles.Dispose();
            Mergers.Dispose();
        }
        
        public struct ParticleBitFlags : IDisposable
        {
            NativeBitArray Flags;
            
            public ParticleBitFlags(int length)
            {
                Flags = new NativeBitArray(length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
            
            public bool this[int index]
            {
                get
                {
                    return Flags.IsSet(index);
                }
                set
                {
                    Flags.Set(index, value);
                }
            }

            public byte GetEmptyIndexIngroup(int groupIndex)
            {
                var zero = groupIndex << 2;
                if (this[zero]) return 0;
                if (this[zero + 1]) return 1;
                if (this[zero + 2]) return 2;
                return 3;
            }

            public void Dispose()
            {
                Flags.Dispose();
            }

            public int LastGroup()
            {
                for (int i = Flags.Length-5; i >= 0; i-=4)
                {
                    if (Flags.TestAny(i, 4)) return i>>2;
                }
                return 0;
            }
        }

        public unsafe struct Merger
        {
            public Transform UnitParticle;
            public ulong NewValue;
            public DNA.Position CurrentTarget;
            public byte GroupIndex => CurrentTarget.GroupIndex;
            public byte InnerGroupIndex => CurrentTarget.InnerGroupIndex;
            public int TargetParticleIndex => CurrentTarget.TargetParticleIndex;
            
            public float T;
            public const float Lifespan = 0.2f;
        
            public static Merger NewParticle(Transform transform, ulong newValue)
            {
                return new Merger()
                {
                    UnitParticle = transform,
                    NewValue = newValue,
                    CurrentTarget = new DNA.Position(0, (byte)((newValue-1)&3))
                };
            }

            public Merger Advance()
            {
                var advance = this;
                // Weird stuff going on here.
                // - We do "NewValue-1" because if we represent '16' and read bits '010000' then we'll advance forward in these group orders: 00->00->01
                // - Instead, by using '16-1==15' we get these bits '001111' meaning we'll advance forward in these groups: 11->11->00
                // As you can see this better represents the movement that we want this particle to follow
                advance.CurrentTarget = new DNA.Position((byte)(GroupIndex+1), (byte)(((NewValue-1)>>((GroupIndex+1)<<1))&3));
                advance.T = 0;
                return advance;
            }
            public Merger CreateFake()
            {
                var fake = this;
                fake.NewValue = 0;
                fake.T = 0;
                return fake;
            }
        }

        public void ApplyChange(ref ulong countup, Change change)
        {
            if (change.Value > 0)
            {
                for (int i = 0; i < change.Value; i++)
                {
                    countup++;
                    Mergers.Add(Merger.NewParticle(change.Transform, countup));
                }
            }
            else if (change.Value < 0)
            {
                // TODO: Add 'sync points'
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
                    if (merger.NewValue == 0)
                    {
                        // Fake used for animations
                    }
                    else if (merger.CurrentTarget.InnerGroupIndex == 3)
                    {
                        // We completed a set. Move all the inners to the empty slot in the thing ahead
                        var targetInnerIndex = Particles.GetEmptyIndexIngroup(merger.GroupIndex+1);
                        // Use the merger as the 'animation source'
                        var adv = merger.Advance();
                        Mergers.Add(adv);
                        // Create 3 fake ones at the expected positions
                        var fake = adv.CreateFake();
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 0)*dnaSpacing); Mergers.Add(fake);
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 1)*dnaSpacing); Mergers.Add(fake);
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 2)*dnaSpacing); Mergers.Add(fake);
                        // Disable particles
                        Particles[merger.TargetParticleIndex] = false;
                        Particles[merger.TargetParticleIndex-1] = false;
                        Particles[merger.TargetParticleIndex-2] = false;
                        Particles[merger.TargetParticleIndex-3] = false;
                    }
                    else if (!Particles[merger.TargetParticleIndex])
                    {
                        Particles[merger.TargetParticleIndex] = true;
                    }
                    else
                    {
                        // Shouldn't need this anymore...
                        //// Push this particle to the next index
                        //Mergers.Add(Merger.MoveParticle(merger.UnitParticle, (byte)(merger.GroupIndex), (byte)(merger.InnerGroupIndex+1), true));
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

    public void Add(ulong change)
    {
        Value += change;
    }
    public void Subtract(ulong change)
    {
        Value = Value - change;
    }
}