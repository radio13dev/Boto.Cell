using Drawing;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using static Unity.Mathematics.math;
using float4x4 = Unity.Mathematics.float4x4;
using quaternion = Unity.Mathematics.quaternion;

public partial struct Visuals : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var builder = DrawingManager.GetBuilder(true);
        var virusDrawJob = new VirusDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        var virusDrawJob_Player = new VirusDrawJob_Player()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(virusDrawJob);
        var cellDrawJob = new CellDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        state.Dependency = JobHandle.CombineDependencies(virusDrawJob_Player, cellDrawJob);
        builder.DisposeAfter(state.Dependency);
    }
    
    [WithNone(typeof(Player))]
    partial struct VirusDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Virus virus, ref Virus.AnimData animData, in Transform position, in Collider collider)
        {
            CommandBuilder.Arrowhead(float3(position.Position, 0), float3(position.Direction, 0), float3(0,0,1), collider.Radius);
            
            var p = float3(position.Position, 0);
            var dir = float3(position.Direction, 0);
            var rotZero = quaternion.LookRotation(dir, float3(0,0,1));
            
            // Display DNA in base 4
            //int len = 1 + (64 - math.lzcnt(virus.DNA.Value))/2;
            //for (int i = 0; i < len; i++)
            //{
            //    const float DNA_SPACING = 1f;
            //    const float DNA_ANGLE_SHIFT = 0.5f;
            //    p -= dir*DNA_SPACING;
            //    var rot = quaternion.AxisAngle(dir, i*DNA_ANGLE_SHIFT);
            //    
            //    var t = LocalTransform.FromPositionRotationScale(p, math.mul(rot, rotZero), collider.Radius/2);
            //    
            //    CommandBuilder.WireRectangle(t.Position, t.Rotation, t.Scale);
            //    
            //    float innerSpacing = t.Scale/4;
            //    float innerScale = t.Scale/5;
            //    const byte b = 0b11;
            //    byte bits = (byte)((virus.DNA.Value>>(i*2))&b);
            //    if (bits > 0) CommandBuilder.WireRectangle(t.Position + t.Forward()*innerSpacing + t.Right()*innerSpacing, t.Rotation, innerScale);
            //    if (bits > 1) CommandBuilder.WireRectangle(t.Position + t.Forward()*innerSpacing - t.Right()*innerSpacing, t.Rotation, innerScale);
            //    if (bits > 2) CommandBuilder.WireRectangle(t.Position - t.Forward()*innerSpacing + t.Right()*innerSpacing, t.Rotation, innerScale);
            //}
        }
    }
        
    [WithAll(typeof(Player))]
    partial struct VirusDrawJob_Player : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Virus virus, ref Virus.AnimData animData, in Transform position, in Collider collider, in DynamicBuffer<DNA.Group> groups, in DynamicBuffer<DNA.Particle> particles, in DynamicBuffer<DNA.Merger> mergers)
        {
            new VirusDrawJob(){CommandBuilder = CommandBuilder}.Execute(virus, ref animData, position, collider);
            //CommandBuilder.Label2D(float3(position.Position - position.Direction, 0), virus.DNA.Value.ToString(), 14*animData.DNATextScale, LabelAlignment.Center);
            animData.TimeSinceDNAChange -= math.clamp(dt*Virus.AnimData.DNAChangeFadeSpeedLinear, -abs(animData.TimeSinceDNAChange), abs(animData.TimeSinceDNAChange));
            animData.TimeSinceDNAChange -= dt*animData.TimeSinceDNAChange*Virus.AnimData.DNAChangeFadeSpeedRelative;
            
            
            // Display DNA in base 4
            for (int i = 0; i < groups.Length; i++)
            {
                var p = float3(groups[i].Transform.Position,0);
                var dir = float3(groups[i].Transform.Direction, 0);
                var rot = quaternion.LookRotation(dir, float3(0,0,1));
                
                var t = LocalTransform.FromPositionRotationScale(p, rot, collider.Radius/2);
                
                CommandBuilder.WireRectangle(t.Position, t.Rotation, t.Scale);
                
                float innerSpacing = t.Scale/4;
                float innerScale = t.Scale/5;
                if (particles[(i<<2) + 0].Active) CommandBuilder.WireRectangle(t.Position + t.Forward()*innerSpacing + t.Right()*innerSpacing, t.Rotation, innerScale);
                if (particles[(i<<2) + 1].Active) CommandBuilder.WireRectangle(t.Position + t.Forward()*innerSpacing - t.Right()*innerSpacing, t.Rotation, innerScale);
                if (particles[(i<<2) + 2].Active) CommandBuilder.WireRectangle(t.Position - t.Forward()*innerSpacing + t.Right()*innerSpacing, t.Rotation, innerScale);
                if (particles[(i<<2) + 3].Active) CommandBuilder.WireRectangle(t.Position - t.Forward()*innerSpacing - t.Right()*innerSpacing, t.Rotation, innerScale);
            }
            
            for (int i = 0; i < mergers.Length; i++)
            {
                float innerSpacing = collider.Radius/8;
                float innerScale = collider.Radius/10;
                
                var merger = mergers[i];
                var targetGroup = groups[merger.DepthToMergeFrom + 1];
                var rot = quaternion.LookRotation(float3(targetGroup.Transform.Direction,0), float3(0,0,1));
                CommandBuilder.WireRectangle(float3(merger.ParticleA,0), rot, innerScale);
                CommandBuilder.WireRectangle(float3(merger.ParticleB,0), rot, innerScale);
                CommandBuilder.WireRectangle(float3(merger.ParticleC,0), rot, innerScale);
                CommandBuilder.WireRectangle(float3(merger.ParticleD,0), rot, innerScale);
            }
        }
    }
    partial struct CellDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Cell virus, in Transform position, in Collider collider)
        {
            CommandBuilder.Circle(float3(position.Position, 0), float3(0,0,1), collider.Radius);
        }
    }
}

public partial struct PlayerControlSystem : ISystem
{
    EntityQuery m_PlayerQuery;
    public void OnCreate(ref SystemState state)
    {
        m_PlayerQuery = SystemAPI.QueryBuilder().WithAll<Input, Player, Transform>().Build();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        var zeroPlane = new Plane(new Vector3(0,0,1), 0);
        var mouseWorldRay = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        zeroPlane.Raycast(mouseWorldRay, out float enter);
        var mouseWorldPos = ((float3)mouseWorldRay.GetPoint(enter)).xy;
        
        float bestDist = float.MaxValue;
        Entity bestEntity = Entity.Null;
        Transform bestTransform = default;
        Collider bestCollider = default;
        foreach (var (transform, collider, entity) in SystemAPI.Query<RefRO<Transform>, RefRO<Collider>>().WithEntityAccess())
        {
            // Find the closest entity to the mouse world pos
            var dist = math.distance(transform.ValueRO.Position.xy, mouseWorldPos);
            if (dist < bestDist && Collider.Overlaps(transform.ValueRO.Position, collider.ValueRO, mouseWorldPos, new Collider()))
            {
                bestDist = dist;
                bestEntity = entity;
                bestTransform = transform.ValueRO;
                bestCollider = collider.ValueRO;
            }
        }
        
        if (bestEntity != Entity.Null)
        {
            var draw = Draw.ingame;
            draw.Circle(float3(bestTransform.Position,0), float3(0,0,1), bestCollider.Radius*1.2f);
        }
    
        new Job()
        {
            mousePos = mouseWorldPos,
            mousePress = Mouse.current.press.wasPressedThisFrame || Mouse.current.rightButton.isPressed,
            mousePressedOnEntity = bestEntity
        }.Schedule();
        
        var p = SystemAPI.GetComponent<Transform>(m_PlayerQuery.GetSingletonEntity()).Position;
        //Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(p, 0), Time.deltaTime);
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(p, -10), Time.deltaTime);
    }
    
    [WithAll(typeof(Player))]
    partial struct Job : IJobEntity
    {
        [ReadOnly] public float2 mousePos;
        [ReadOnly] public bool mousePress;
        [ReadOnly] public Entity mousePressedOnEntity;
        
        public void Execute(in Transform position, ref Input input)
        {
            input.Vec0 = mousePos - position.Position;
            if (mousePress)
            {
                input.Action = 1;
                input.ActionRef = mousePressedOnEntity;
            }
        }
    }
}