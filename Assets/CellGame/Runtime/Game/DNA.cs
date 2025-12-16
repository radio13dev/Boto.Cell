using System;
using System.Diagnostics.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

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
        public readonly long Value;

        public Change(Transform transform, long v)
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
        public ulong ParticleValue => (ulong)InnerGroupIndex*(ulong)((ulong)2<<(GroupIndex));

        public static Position CreateFromParticleIndex(int index)
        {
            return new Position((byte)(index>>2), (byte)(index&3));
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
                        Groups  = new NativeArray<Transform>(ParticleBitFlags.GroupCount, Allocator.Persistent),
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
            // Each ulong is 64 bits, each group is 4 bits. IE: a ulong contains 16 groups.
            // We need 256*4 bits to represent the 256*4 potential particles that will be displayed
            public const int GroupCount = 32;
            public const int FlagArrayLength = GroupCount*4;
            fixed bool _flags[FlagArrayLength];
            public int Length => FlagArrayLength;
            
            public bool this[int index]
            {
                get => this[(uint)index];
                set => this[(uint)index] = value;
            }
            public bool this[uint index]
            {
                get
                {
                    return _flags[index];
                }
                set
                {
                    _flags[index] = value;
                }
            }

            private static ParticleBitFlags CreateFromValue(ulong value)
            {
                ParticleBitFlags flags = new();
                for (int groupIndex = 0; groupIndex < GroupCount; groupIndex++)
                {
                    var index = groupIndex*4;
                    var set = ((value>>groupIndex)>>groupIndex)&3;
                    flags[index] = set > 0;
                    flags[index+1] = set > 1;
                    flags[index+2] = set > 2;
                }
                return flags;
            }

            public byte GetEmptyIndexIngroup(int groupIndex)
            {
                var index = groupIndex*4;
                if (!this[index]) return 0;
                if (!this[index + 1]) return 1;
                if (!this[index + 2]) return 2;
                return 3;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="removed"></param>
            /// <param name="disabled">Includes the 'particleTaken' flag</param>
            /// <param name="targets"></param>
            /// <param name="particleTaken"></param>
            /// <exception cref="NotImplementedException"></exception>
            [Pure]
            public void Subtract(ulong removed, out ParticleBitFlags disabled, out ParticleBitFlags targets, out Position particleTaken)
            {
                ulong value = 0;
                for (int groupIndex = 0; groupIndex < GroupCount; groupIndex++)
                {
                    var index = groupIndex*4;
                    if (this[index])    value += (1ul<<groupIndex)<<groupIndex;
                    if (this[index+1])  value += (1ul<<groupIndex)<<groupIndex;
                    if (this[index+2])  value += (1ul<<groupIndex)<<groupIndex;
                }
                value -= removed;
                ParticleBitFlags result = ParticleBitFlags.CreateFromValue(value);
                
                GetDifference(this, result, out disabled, out targets);
                int consumedParticlePosition;
                for (consumedParticlePosition = disabled.Length-1; consumedParticlePosition >= 0; consumedParticlePosition--)
                {
                    if (disabled[consumedParticlePosition]) break;
                }
                particleTaken = Position.CreateFromParticleIndex(consumedParticlePosition);
            }

            public static void GetDifference(ParticleBitFlags a, ParticleBitFlags b, out ParticleBitFlags disabled, out ParticleBitFlags enabled)
            {
                disabled = new();
                for (int i = 0; i < disabled.Length; i++)
                {
                    disabled[i] = a[i] && !b[i];
                    
                }
                
                enabled = new();
                for (int i = 0; i < enabled.Length; i++)
                    enabled[i] = !a[i] && b[i];
            }
        }

        public unsafe struct Merger
        {
            public Transform UnitParticle;
            public ulong NewValue;
            public DNA.Position CurrentTarget;
            public bool RemoveFlag;
            
            public byte GroupIndex => CurrentTarget.GroupIndex;
            public byte InnerGroupIndex => CurrentTarget.InnerGroupIndex;
            public int TargetParticleIndex => CurrentTarget.TargetParticleIndex;
            
            public float T;
            public const float Lifespan = 0.2f;
            public const ulong SyncPointFlag = ulong.MaxValue;
        
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

            // A SyncPoint lets all ongoing animations end, and forces all ongoing animations to insert new Mergers 'before' it.
            public static Merger SyncPoint()
            {
                return new Merger()
                {
                    NewValue = SyncPointFlag
                };
            }

            public static Merger RemoveParticles(Transform changeTransform, uint removed)
            {
                return new Merger()
                {
                    UnitParticle = changeTransform,
                    RemoveFlag = true,
                    NewValue = removed
                };
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
                Mergers.Add(Merger.SyncPoint());
                Mergers.Add(Merger.RemoveParticles(change.Transform, (uint)(-change.Value)));
                Mergers.Add(Merger.SyncPoint());
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
        
        public void UpdateMergers(ref Random random, float dt, Transform transform, Collider collider)
        {
            // Find any sync points
            int syncPointEndOffset = 0;
            for (int i = 0; i < Mergers.Length; i++)
            {
                if (Mergers[i].NewValue == Merger.SyncPointFlag)
                {
                    syncPointEndOffset = Mergers.Length - i;
                    break;
                }
            }
            
            for (int i = 0; i < Mergers.Length; i++)
            {
                ref var merger = ref Mergers.ElementAt(i);
                if (merger.NewValue == Merger.SyncPointFlag)
                {
                    if (i == 0)
                    {
                        // Delete SyncPoints that are at the start of the list
                        Mergers.RemoveAt(i);
                        i--;
                    }
                    continue;
                }
                
                if (i > Mergers.Length-syncPointEndOffset)
                {
                    // Time paused, move randomly (todo)
                    merger.UnitParticle.Position += random.NextFloat2(-1f*dt, 1f*dt);
                    continue;
                }
                
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
                    else if (merger.RemoveFlag)
                    {
                        // Determine the difference between what we have, and what we should have. Animate accordingly.
                        Particles.Subtract(merger.NewValue, out ParticleBitFlags disabled, out ParticleBitFlags targets, out Position particleTaken);
                        for (int flagIndex = 0; flagIndex < disabled.Length; flagIndex++)
                            if (disabled[flagIndex]) Particles[flagIndex] = false;
                        
                        var takenZero = Groups[particleTaken.GroupIndex];
                        takenZero = takenZero.Offset(GetOffset(takenZero, particleTaken.InnerGroupIndex));
                        for (int flagIndex = 0; flagIndex < targets.Length; flagIndex++)
                        {
                            if (targets[flagIndex])
                            {
                                Mergers.InsertRangeWithBeginEnd(Mergers.Length-syncPointEndOffset, Mergers.Length-syncPointEndOffset+1);
                                Mergers[Mergers.Length-syncPointEndOffset-1] = new Merger()
                                {
                                    UnitParticle = takenZero,
                                    CurrentTarget = Position.CreateFromParticleIndex(flagIndex),
                                    NewValue = 1 // Should enable it... hopefully...
                                };
                            }
                        }
                        
                    }
                    else if (merger.CurrentTarget.InnerGroupIndex == 3)
                    {
                        // We completed a set. Move all the inners to the empty slot in the thing ahead
                        // Use the merger as the 'animation source'
                        var adv = merger.Advance();
                        var fake = adv.CreateFake(); // Create 3 fake ones at the expected positions
                        
                        Mergers.InsertRangeWithBeginEnd(Mergers.Length-syncPointEndOffset, Mergers.Length-syncPointEndOffset+4);
                        Mergers[Mergers.Length-syncPointEndOffset-4] = adv;
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 0)*dnaSpacing); Mergers[Mergers.Length-syncPointEndOffset-3] = fake;
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 1)*dnaSpacing); Mergers[Mergers.Length-syncPointEndOffset-2] = fake;
                        fake.UnitParticle = zero.Offset(GetOffset(zero, 2)*dnaSpacing); Mergers[Mergers.Length-syncPointEndOffset-1] = fake;
                        
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