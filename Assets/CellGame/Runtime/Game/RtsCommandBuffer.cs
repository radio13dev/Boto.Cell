using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[StructLayout(LayoutKind.Explicit, Size = sizeof(byte) + sizeof(float) * 3, Pack = 2)]
public readonly struct RtsCommandBuffer : IBufferElementData
{
    public const int MaxBufferSize = 200;

    public enum eType : byte
    {
        Move,
        Interact
    }

    [FieldOffset(0)] public readonly eType Type;
    [FieldOffset(1)] public readonly Entity Entity;
    [FieldOffset(1)] public readonly float2 float2;


    public static RtsCommandBuffer Interact(Entity entity) => new RtsCommandBuffer(entity);

    RtsCommandBuffer(Entity entity) : this()
    {
        Type = eType.Interact;
        Entity = entity;
    }

    public static RtsCommandBuffer Move(float2 position) => new RtsCommandBuffer(position);

    RtsCommandBuffer(float2 position) : this()
    {
        Type = eType.Move;
        float2 = position;
    }

    public struct Memory : IComponentData
    {
        public float NearestDistance;

        public void Clear()
        {
            NearestDistance = float.MaxValue;
        }
    }

    public float2 GetPosition(ref ComponentLookup<Transform> transformLookup)
    {
        switch (Type)
        {
            case eType.Move:
                return float2;
            case eType.Interact:
                if (transformLookup.TryGetComponent(Entity, out var target))
                    return target.Position;
                else
                    break;
        }

        return default;
    }

    public void Execute(ref Memory memory, ref ComponentLookup<Transform> TransformLookup, in Transform transform, in Collider collider, ref Input input, out bool finished)
    {
        finished = false;
        switch (Type)
        {
            case RtsCommandBuffer.eType.Move:
            {
                var d = math.distance(transform.Position, float2);
                if (d > collider.Radius)
                {
                    memory.NearestDistance = d;

                    input.Action = 1;
                    input.Vec0 = float2 - transform.Position;
                }
                else
                {
                    finished = true;
                }

                break;
            }
            case RtsCommandBuffer.eType.Interact:
            {
                if (TransformLookup.TryGetComponent(Entity, out var target))
                {
                    input.Action = 1;
                    input.ActionRef = Entity;
                    input.Vec0 = TransformLookup[Entity].Position - transform.Position;
                }
                else
                {
                    finished = true;
                }

                break;
            }
            default:
                finished = true;
                return;
        }
    }
}

[UpdateBefore(typeof(InputSystem2))]
public partial struct RtsInputProcessSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        new Job()
        {
            TransformLookup = SystemAPI.GetComponentLookup<Transform>(true)
        }.Schedule();
    }

    partial struct Job : IJobEntity
    {
        [ReadOnly] public ComponentLookup<Transform> TransformLookup;

        public void Execute(ref DynamicBuffer<RtsCommandBuffer> commands, ref RtsCommandBuffer.Memory memory, in Transform transform, in Collider collider, ref Input input)
        {
            if (commands.Length == 0) return;

            var command = commands[0];
            command.Execute(ref memory, ref TransformLookup, in transform, in collider, ref input, out bool finished);
            if (finished)
            {
                memory.Clear();
                commands.RemoveAt(0);
            }
        }
    }
}