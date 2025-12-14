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
    public readonly struct Changes : IComponentData
    {
        [NativeDisableContainerSafetyRestriction]
        public readonly NativeList<Change> Data;
        
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

    public struct AnimData : IComponentData
    {
        public const float DNAChangeFadeSpeedLinear = 2;
        public const float DNAChangeFadeSpeedRelative = 10;
        public float TimeSinceDNAChange;
        public float DNATextScale => 1 + TimeSinceDNAChange;

        [NativeDisableContainerSafetyRestriction]
        public NativeList<Transform> Groups;

        [NativeDisableContainerSafetyRestriction]
        public NativeList<bool> Particles;

        [NativeDisableContainerSafetyRestriction]
        public NativeList<Merger> Mergers;

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
        
        public void UpdateMergers(float dt)
        {
            for (int i = 0; i < Mergers.Length; i++)
            {
                var merger = Mergers[i];
                merger.T += dt;
                if (merger.T >= Merger.Lifespan)
                {
                    if (merger.ChangeCount > 0)
                    {
                        if (!Particles[merger.TargetParticleIndex])
                        {
                            Particles[merger.TargetParticleIndex] = true;
                        }
                        else
                        {
                            if (merger.InnerGroupIndex == 2)
                            {
                                // We completed a set
                                var zero = Groups[merger.GroupIndex];
                                Mergers.Add(Merger.MoveParticle(merger.UnitParticle, merger.GroupIndex+1, 0, true));
                                Mergers.Add(Merger.MoveParticle(merger.UnitParticle, merger.GroupIndex+1, 0, false));
                                Mergers.Add(Merger.MoveParticle(merger.UnitParticle, merger.GroupIndex+1, 0, false));
                                Mergers.Add(Merger.MoveParticle(merger.UnitParticle, merger.GroupIndex+1, 0, false));
                            }
                            // Push this particle to the next index
                            Mergers.Add(Merger.MoveParticle(merger.UnitParticle, merger.TargetParticleIndex+1));
                        }
                    }
                    
                }
            }
        }
    }
}