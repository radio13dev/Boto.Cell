using Drawing;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using static Unity.Mathematics.math;
using float2 = Unity.Mathematics.float2;
using float3 = Unity.Mathematics.float3;
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
        var dnaDrawJob = new DnaDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        var cellDrawJob = new CellDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        var RtsCommandDrawJob = new RtsCommandDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            TransformLookup = SystemAPI.GetComponentLookup<Transform>(true),
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        state.Dependency = JobHandle.CombineDependencies(virusDrawJob, dnaDrawJob, cellDrawJob);
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, RtsCommandDrawJob);
        builder.DisposeAfter(state.Dependency);
    }
    
    partial struct VirusDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Virus virus, ref Virus.AnimData animData, in Transform position, in Collider collider)
        {
            CommandBuilder.Arrowhead(float3(position.Position, 0), float3(position.Direction, 0), float3(0,0,1), collider.Radius);
            //CommandBuilder.Label2D(float3(position.Position - position.Direction, 0), virus.DNA.Value.ToString(), 14*animData.DNATextScale, LabelAlignment.Center);
            animData.TimeSinceDNAChange -= math.clamp(dt*Virus.AnimData.DNAChangeFadeSpeedLinear, -abs(animData.TimeSinceDNAChange), abs(animData.TimeSinceDNAChange));
            animData.TimeSinceDNAChange -= dt*animData.TimeSinceDNAChange*Virus.AnimData.DNAChangeFadeSpeedRelative;
        }
    }
        
    partial struct DnaDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Transform position, in Collider collider, in DynamicBuffer<DNA.Group> groups, in DynamicBuffer<DNA.Particle> particles, in DynamicBuffer<DNA.Merger> mergers)
        {
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
    partial struct RtsCommandDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentLookup<Transform> TransformLookup;
        public CommandBuilder CommandBuilder;
        
        public void Execute(in Transform position, in DynamicBuffer<RtsCommandBuffer> commands, in RtsCommandBuffer.Memory memory)
        {
            float2 last = position.Position;
            for (int i = 0; i < commands.Length; i++)
            {
                float2 newPos = commands[i].GetPosition(ref TransformLookup);
                CommandBuilder.DashedLine(float3(last,0), float3(newPos, 0), 0.5f, 0.5f);
                last = newPos;
            }
        }
    }
}

public partial struct PlayerControlSystem : ISystem
{
    EntityQuery m_PlayerQuery;
    NativeHashSet<Entity> m_Selected;
    
    bool m_IsDragging;
    float2 m_DragStartPos;
    float m_Zoom;
    const float ZoomRate = 100f;
    float CameraTrackRate => m_RtsModeEnabled ? 20 : 10;
    float KeyboardPanRate => 5f;
    
    bool m_RtsModeEnabled;
    
    bool m_IsCameraDragging;
    
    float2 m_FocusedCameraTargetPos;
    float m_FocusedCameraTargetZoom;
    float2 m_RtsCameraTargetPos;
    float m_RtsCameraTargetZoom;
    
    public void OnCreate(ref SystemState state)
    {
        m_PlayerQuery = SystemAPI.QueryBuilder().WithAll<Input, Player, Transform>().Build();
        m_Selected = new NativeHashSet<Entity>(1024, Allocator.Persistent);
        m_Zoom = 10;
    }

    public void OnDestroy(ref SystemState state)
    {
        m_Selected.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (m_RtsModeEnabled)
            {
                m_RtsCameraTargetZoom = m_Zoom;
                m_Zoom = m_FocusedCameraTargetZoom == 0 ? m_Zoom*0.9f : m_FocusedCameraTargetZoom;
            }
            else
            {
                m_FocusedCameraTargetZoom = m_Zoom;
                m_Zoom = m_RtsCameraTargetZoom == 0 ? m_Zoom*1.1f : m_RtsCameraTargetZoom;
            }
            
            m_RtsModeEnabled = !m_RtsModeEnabled;
        }
        m_Zoom = math.clamp(m_Zoom - Mouse.current.scroll.y.value*ZoomRate*SystemAPI.Time.DeltaTime*math.sqrt(m_Zoom), 10, 3000);
    
        var draw = Draw.ingame;
        
        var mouseWorldPos = ConvertScreenPointToWorldPos(Mouse.current.position.ReadValue());
        var mouseWorldDelta = mouseWorldPos - ConvertScreenPointToWorldPos(Mouse.current.position.ReadValue() - Mouse.current.delta.ReadValue());
        
        // Setup selection box
        if (m_RtsModeEnabled)
            if (!m_IsDragging)
            {
                if (Mouse.current.press.wasPressedThisFrame)
                {
                    m_IsDragging = true;
                    m_DragStartPos = mouseWorldPos;
                }
            }
            else
            {
                if (!Mouse.current.press.isPressed)
                    m_IsDragging = false;
            }
        if (m_IsDragging && !Keyboard.current.shiftKey.isPressed) m_Selected.Clear();
        var selectionBoxCenter = (m_DragStartPos + mouseWorldPos)/2;
        var selectionBoxSize = math.abs(mouseWorldPos - m_DragStartPos);
        var selectionBox = new Rect(selectionBoxCenter-selectionBoxSize/2, selectionBoxSize);
        
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
                
                if (m_RtsModeEnabled && Mouse.current.press.wasPressedThisFrame)
                    m_Selected.Add(entity);
            }
            
            var overlapBox = new Rect(transform.ValueRO.Position - collider.ValueRO.Radius, float2(collider.ValueRO.Radius*2));
            if (m_IsDragging && selectionBox.Overlaps(overlapBox) && selectionBox.Contains(mathu.MoveTowards(transform.ValueRO.Position, selectionBox.center, collider.ValueRO.Radius)))
            {
                m_Selected.Add(entity);
            }
            
            if (m_RtsModeEnabled && m_Selected.Contains(entity))
            {
                draw.Circle(float3(transform.ValueRO.Position,0), float3(0,0,1), collider.ValueRO.Radius*1.1f);
            }
        }
        
        if (bestEntity != Entity.Null)
        {
            draw.Circle(float3(bestTransform.Position,0), float3(0,0,1), bestCollider.Radius*1.2f);
        }
        
        float2 cameraTargetPos;
    
        if (!m_RtsModeEnabled)
        {
            // Commands are sent to player using rts job 'action' operation
            if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.isPressed)
                new RtsPlayerActionJob()
                {
                    Command = bestEntity == Entity.Null ? RtsCommandBuffer.Move(mouseWorldPos) : RtsCommandBuffer.Interact(bestEntity),
                    TransformLookup = SystemAPI.GetComponentLookup<Transform>(true)
                }.Schedule();
            
            // Use this middle-click-drag to move the camera around
            if (Mouse.current.middleButton.isPressed)
            {
                m_FocusedCameraTargetPos -= mouseWorldDelta;
                m_IsCameraDragging = true;
            }

            if (Keyboard.current.wKey.isPressed)
            {
                m_FocusedCameraTargetPos += float2(0, 1) * SystemAPI.Time.DeltaTime * KeyboardPanRate * m_Zoom;
                m_IsCameraDragging = true;
            }

            if (Keyboard.current.sKey.isPressed)
            {
                m_FocusedCameraTargetPos += float2(0, -1) * SystemAPI.Time.DeltaTime * KeyboardPanRate * m_Zoom;
                m_IsCameraDragging = true;
            }

            if (Keyboard.current.dKey.isPressed)
            {
                m_FocusedCameraTargetPos += float2(1, 0) * SystemAPI.Time.DeltaTime * KeyboardPanRate * m_Zoom;
                m_IsCameraDragging = true;
            }

            if (Keyboard.current.aKey.isPressed)
            {
                m_FocusedCameraTargetPos += float2(-1, 0) * SystemAPI.Time.DeltaTime * KeyboardPanRate * m_Zoom;
                m_IsCameraDragging = true;
            }

            // If we're not dragging, focus on player
            if (!m_IsCameraDragging)
            {
                var playerE = m_PlayerQuery.GetSingletonEntity();
                m_FocusedCameraTargetPos = SystemAPI.GetComponent<Transform>(playerE).Position;
            }
            else
            {
                // Release 'drag' when we click on empty space
                if ((Mouse.current.press.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame) && bestEntity == Entity.Null)
                    m_IsCameraDragging = false;
            }
            
            cameraTargetPos = m_FocusedCameraTargetPos;
            if (math.all(m_RtsCameraTargetPos == float2.zero)) m_RtsCameraTargetPos = cameraTargetPos;
        }
        else
        {
            // Draw bounding box
            if (m_IsDragging)
                draw.WireRectangle(float3(selectionBox.center,0), quaternion.Euler(math.PIHALF,0,0), selectionBox.size);
            
            // Use this middle-click-drag to move the camera around
            if (Mouse.current.middleButton.isPressed)
                m_RtsCameraTargetPos -= mouseWorldDelta;
            if (Keyboard.current.wKey.isPressed)
                m_RtsCameraTargetPos += float2(0,1)*SystemAPI.Time.DeltaTime*KeyboardPanRate*m_Zoom;
            if (Keyboard.current.sKey.isPressed)
                m_RtsCameraTargetPos += float2(0,-1)*SystemAPI.Time.DeltaTime*KeyboardPanRate*m_Zoom;
            if (Keyboard.current.dKey.isPressed)
                m_RtsCameraTargetPos += float2(1,0)*SystemAPI.Time.DeltaTime*KeyboardPanRate*m_Zoom;
            if (Keyboard.current.aKey.isPressed)
                m_RtsCameraTargetPos += float2(-1,0)*SystemAPI.Time.DeltaTime*KeyboardPanRate*m_Zoom;
                
            cameraTargetPos = m_RtsCameraTargetPos;
            
            // Send commands to all selected units on right-click
            if (Mouse.current.rightButton.isPressed && m_Selected.Count > 0)
            {
                // Right click sends commands
                new RtsSendJob()
                {
                    Affected = m_Selected,
                    Append = Keyboard.current.shiftKey.isPressed,
                    Command = bestEntity == Entity.Null ? RtsCommandBuffer.Move(mouseWorldPos) : RtsCommandBuffer.Interact(bestEntity),
                    
                }.Schedule();
            }
            
        }
        
        
        //Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(p, 0), Time.deltaTime);
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(cameraTargetPos, -m_Zoom), Time.deltaTime*CameraTrackRate);
        Camera.main.farClipPlane = math.max(Camera.main.nearClipPlane+0.01f, -Camera.main.transform.position.z*1.5f);
    }

    private static float2 ConvertScreenPointToWorldPos(Vector2 val)
    {
        var zeroPlane = new Plane(new Vector3(0,0,1), 0);
        var mouseWorldRay = Camera.main.ScreenPointToRay(val);
        zeroPlane.Raycast(mouseWorldRay, out float enter);
        return ((float3)mouseWorldRay.GetPoint(enter)).xy;
    }

    [WithAll(typeof(Player))]
    partial struct RtsPlayerActionJob : IJobEntity
    {
        [ReadOnly] public RtsCommandBuffer Command;
        [ReadOnly] public ComponentLookup<Transform> TransformLookup;
        
        public void Execute(in Entity entity, in Transform transform, in Collider collider, ref Input input, ref DynamicBuffer<RtsCommandBuffer> buffer, ref RtsCommandBuffer.Memory memory)
        {
            Command.Execute(ref memory, ref TransformLookup, transform, collider, ref input, out bool finished);
            memory.Clear();
            buffer.Clear();
        }
    }

    partial struct RtsSendJob : IJobEntity
    {
        [ReadOnly] public NativeHashSet<Entity> Affected;
        [ReadOnly] public RtsCommandBuffer Command;
        [ReadOnly] public bool Append;
        
        public void Execute(in Entity entity, ref DynamicBuffer<RtsCommandBuffer> buffer, ref RtsCommandBuffer.Memory memory)
        {
            if (!Affected.Contains(entity)) return;
            
            if (!Append)
            {
                buffer.Clear();
                memory.Clear();
            }
            
            if (buffer.Length < RtsCommandBuffer.MaxBufferSize)
                buffer.Add(Command);
        }
    }
}