using Drawing;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static Unity.Mathematics.math;
using float2 = Unity.Mathematics.float2;
using float3 = Unity.Mathematics.float3;
using float4x4 = Unity.Mathematics.float4x4;
using quaternion = Unity.Mathematics.quaternion;

public partial struct Visuals : ISystem
{
    EntityQuery m_PlayerTransformQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DNA.AnimData>();
        state.RequireForUpdate<Player>();
        
        m_PlayerTransformQuery = SystemAPI.QueryBuilder().WithAll<Player, Transform, Collider>().Build();
    }

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
            Zero = m_PlayerTransformQuery.GetSingleton<Transform>(),
            ZeroCollider = m_PlayerTransformQuery.GetSingleton<Collider>(),
            Dna = SystemAPI.GetSingleton<DNA>(),
            AnimData = SystemAPI.GetSingleton<DNA.AnimData>(),
            
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        var cellDrawJob = new CellDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        var rtsCommandDrawJob = new RtsCommandDrawJob()
        {
            dt = SystemAPI.Time.DeltaTime,
            TransformLookup = SystemAPI.GetComponentLookup<Transform>(true),
            CommandBuilder = builder
        }.Schedule(state.Dependency);
        state.Dependency = JobHandle.CombineDependencies(virusDrawJob, dnaDrawJob, cellDrawJob);
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, rtsCommandDrawJob);
        builder.DisposeAfter(state.Dependency);
    }
    
    partial struct VirusDrawJob : IJobEntity
    {
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute(in Virus virus, ref Virus.AnimData animData, in Transform position, in Collider collider)
        {
            CommandBuilder.Arrowhead(float3(position.Position, 0), float3(position.Direction, 0), float3(0,0,1), collider.Radius);
        }
    }
        
    partial struct DnaDrawJob : IJob
    {
        [ReadOnly] public Transform Zero;
        [ReadOnly] public Collider ZeroCollider;
        [ReadOnly] public DNA Dna;
        [ReadOnly] public DNA.AnimData AnimData;
        
        [ReadOnly] public float dt;
        public CommandBuilder CommandBuilder;
        public void Execute()
        {
            //CommandBuilder.Label2D(float3(position.Position - position.Direction, 0), virus.DNA.Value.ToString(), 14*animData.DNATextScale, LabelAlignment.Center);
            AnimData.TimeSinceDNAChange -= math.clamp(dt*DNA.AnimData.DNAChangeFadeSpeedLinear, -abs(AnimData.TimeSinceDNAChange), abs(AnimData.TimeSinceDNAChange));
            AnimData.TimeSinceDNAChange -= dt*AnimData.TimeSinceDNAChange*DNA.AnimData.DNAChangeFadeSpeedRelative;
            
            // Display DNA in base 4
            var groups = AnimData.Groups;
            var particles = AnimData.Particles;
            var mergers = AnimData.Mergers;
            
            var lastGroup = math.max((1 + 64 - math.lzcnt(Dna.Value)) / 2, 1);;
            
            for (int i = 0; i < groups.Length && i < lastGroup; i++)
            {
                var group = groups[i];
                var p = float3(group.Position,0);
                var dir = float3(group.Direction, 0);
                var rot = quaternion.LookRotation(dir, float3(0,0,1));
                
                var dnaGroupScale = DNA.AnimData.GetDnaGroupScale(ZeroCollider);
                var dnaScale = DNA.AnimData.GetDnaScale(ZeroCollider);
                var dnaSpacing = DNA.AnimData.GetDnaSpacing(ZeroCollider);
                
                var t = LocalTransform.FromPositionRotationScale(p, rot, dnaGroupScale);
                
                CommandBuilder.WireRectangle(t.Position, t.Rotation, t.Scale);
                
                if (particles[(i<<2) + 0]) CommandBuilder.WireRectangle(float3(group.Position + DNA.AnimData.GetOffset(group, 0)*dnaSpacing, 0), t.Rotation, dnaScale);
                if (particles[(i<<2) + 1]) CommandBuilder.WireRectangle(float3(group.Position + DNA.AnimData.GetOffset(group, 1)*dnaSpacing, 0), t.Rotation, dnaScale);
                if (particles[(i<<2) + 2]) CommandBuilder.WireRectangle(float3(group.Position + DNA.AnimData.GetOffset(group, 2)*dnaSpacing, 0), t.Rotation, dnaScale);
                if (particles[(i<<2) + 3]) CommandBuilder.WireRectangle(float3(group.Position + DNA.AnimData.GetOffset(group, 3)*dnaSpacing, 0), t.Rotation, dnaScale);
            }
            
            for (int i = 0; i < mergers.Length; i++)
            {
                var dnaScale = DNA.AnimData.GetDnaScale(ZeroCollider);
                var merger = mergers[i];
                var rot = quaternion.LookRotation(float3(merger.UnitParticle.Direction,0), float3(0,0,1));
                CommandBuilder.WireRectangle(float3(merger.UnitParticle.Position,0), rot, dnaScale);
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

public partial class PlayerControlSystem : SystemBase
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
    
    InputSystem_Actions m_InputSystem;
    Vector2 m_lastMousePos;

    protected override void OnCreate()
    {
        m_PlayerQuery = SystemAPI.QueryBuilder().WithAll<Input, Player, Transform>().Build();
        m_Selected = new NativeHashSet<Entity>(1024, Allocator.Persistent);
        m_Zoom = 10;
        
        m_InputSystem = new InputSystem_Actions();
        m_InputSystem.Enable();
    }

    protected override void OnDestroy()
    {
        m_InputSystem.Dispose();
        m_Selected.Dispose();
    }

    protected override void OnUpdate()
    {
        if (TabButton.TabToggleRequested)
        {
            TabButton.TabToggleRequested = false;
            m_IsCameraDragging = false;
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
        m_Zoom = math.clamp(m_Zoom - m_InputSystem.UI.Zoom.ReadValue<float>()*ZoomRate*SystemAPI.Time.DeltaTime*math.sqrt(m_Zoom), 10, 3000);
    
        var draw = Draw.ingame;
        
        var mouseWorldPos = ConvertScreenPointToWorldPos(m_InputSystem.UI.Point.ReadValue<Vector2>());
        var mouseWorldDelta = mouseWorldPos - ConvertScreenPointToWorldPos(m_lastMousePos);
        m_lastMousePos = m_InputSystem.UI.Point.ReadValue<Vector2>();
        
        // Setup selection box
        if (m_RtsModeEnabled)
            if (!m_IsDragging)
            {
                if (m_InputSystem.UI.Click.WasPressedThisFrame())
                {
                    m_IsDragging = true;
                    m_DragStartPos = mouseWorldPos;
                }
            }
            else
            {
                if (!m_InputSystem.UI.Click.IsPressed())
                    m_IsDragging = false;
            }
        if (m_IsDragging && !m_InputSystem.Player.Sprint.IsPressed()) m_Selected.Clear();
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
                
                if (m_RtsModeEnabled && m_InputSystem.UI.Click.WasPressedThisFrame())
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
        
        var playerE = m_PlayerQuery.GetSingletonEntity();
        
        float2 cameraTargetPos;
        if (!m_RtsModeEnabled)
        {
            if (bestEntity == playerE && m_InputSystem.UI.Click.WasReleasedThisFrame())
            {
                Shop.Instance.Toggle();
            }
            
            // Commands are sent to player using rts job 'action' operation
            if (m_InputSystem.UI.Click.WasPressedThisFrame() || m_InputSystem.UI.RightClick.IsPressed())
                new RtsPlayerActionJob()
                {
                    Command = bestEntity == Entity.Null ? RtsCommandBuffer.Move(mouseWorldPos) : RtsCommandBuffer.Interact(bestEntity),
                    TransformLookup = SystemAPI.GetComponentLookup<Transform>(true)
                }.Schedule();
            
            // Use this middle-click-drag to move the camera around
            if (m_InputSystem.UI.MiddleClick.IsPressed() || m_InputSystem.UI.PanTouch.IsPressed())
            {
                m_FocusedCameraTargetPos -= mouseWorldDelta;
                m_IsCameraDragging = true;
            }
    
    
            var dir = m_InputSystem.Player.Move.ReadValue<Vector2>();
            if (dir != Vector2.zero)
            {
                m_FocusedCameraTargetPos += float2(dir) * SystemAPI.Time.DeltaTime * KeyboardPanRate * m_Zoom;
                m_IsCameraDragging = true;
            }

            // If we're not dragging, focus on player
            if (!m_IsCameraDragging)
            {
                m_FocusedCameraTargetPos = SystemAPI.GetComponent<Transform>(playerE).Position;
            }
            else
            {
                // Release 'drag' when we click on empty space
                if ((m_InputSystem.UI.Click.WasPressedThisFrame() || m_InputSystem.UI.RightClick.WasPressedThisFrame()) && bestEntity == Entity.Null)
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
            if (m_InputSystem.UI.MiddleClick.IsPressed() || m_InputSystem.UI.PanTouch.IsPressed())
                m_RtsCameraTargetPos -= mouseWorldDelta;
                
            var dir = m_InputSystem.Player.Move.ReadValue<Vector2>();
            if (dir != Vector2.zero)
                m_RtsCameraTargetPos += float2(dir)*SystemAPI.Time.DeltaTime*KeyboardPanRate*m_Zoom;
                
            cameraTargetPos = m_RtsCameraTargetPos;
            
            // Send commands to all selected units on right-click
            if (m_InputSystem.UI.RightClick.IsPressed() && m_Selected.Count > 0)
            {
                // Right click sends commands
                new RtsSendJob()
                {
                    Affected = m_Selected,
                    Append = m_InputSystem.Player.Sprint.IsPressed(),
                    Command = bestEntity == Entity.Null ? RtsCommandBuffer.Move(mouseWorldPos) : RtsCommandBuffer.Interact(bestEntity),
                    
                }.Schedule();
            }
            
        }
        
        
        //Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(p, 0), Time.deltaTime);
        Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, float3(cameraTargetPos, -m_Zoom), SystemAPI.Time.DeltaTime*CameraTrackRate);
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