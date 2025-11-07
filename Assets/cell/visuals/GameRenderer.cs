using System;
using cell.game;
using Drawing;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevelPhysics2D;
using static Unity.Mathematics.math;
using Input = cell.game.Input;

namespace cell.visuals
{
    public class GameRenderer : MonoBehaviour
    {
        public Camera camera;
        
        State State;
        Visuals Visuals;
        //InputSystem_Actions Inputs;

        private void Start()
        {
            State = new State((uint)DateTime.UtcNow.Ticks, 20, 20);
            //Inputs = new InputSystem_Actions();
            //Inputs.Player.Enable();
            //Inputs.UI.Enable();
        }

        private void OnDestroy()
        {
            if (State) State.Dispose();
        }

        private unsafe void Update()
        {
            // Determine click state
            Input input = default; //GetVirusInput((Vector2)camera.ScreenToWorldPoint((Vector3)Mouse.current.position.ReadValue()), Mouse.current.leftButton.wasPressedThisFrame, in State, out var cellHoveredOver);
            
            State.InjectInput(Id<Virus>.Index(0), input);
            State.Update(Time.deltaTime);
        
            camera.transform.position = 
                Vector3.Lerp(camera.transform.position, float3(State.Viruses[Id<Virus>.Index(0)]->Position, 0), Time.deltaTime);
                
            
            
            var draw = Draw.ingame;
            {
                {
                    var it = State.Viruses.Iterate;
                    while (++it)
                    {
                        ref var virus = ref *it.Value;
                        Visuals.Viruses[it.Index]->Render(ref draw, virus, Time.deltaTime);
                        
                        // Draw + Update the visual 'chain' behind this
                        
                        
                    }
                }
                
                {
                    var it = State.Cells.Iterate;
                    while (++it)
                    {
                        ref var cell = ref *it.Value;
                        draw.Circle(float3(cell.Position, 0), float3(0,0,-1), Cell.Radius);
                        
                        //if (cellHoveredOver == it.Index)
                        {
                            draw.Circle(float3(cell.Position, 0), float3(0,0,-1), Cell.Radius*1.2f, Color.red);
                        }
                    }
                }
            }
        }

        private static unsafe Input GetVirusInput(float2 mousePos, bool leftClick, in State state, out Id<Cell> hoveredOver)
        {
            ref var virus = ref *state.Viruses[Id<Virus>.Index(0)];
            
            // Check if we clicked on something
            hoveredOver = Id<Cell>.Null;
            var bestD = Cell.Radius*Cell.Radius;
            var cellIt = state.Cells.Iterate;
            while (++cellIt)
            {
                ref var cell = ref *cellIt.Value;
                var d = lengthsq(cell.Position - mousePos);
                if (d < bestD)
                {
                    hoveredOver = cellIt.Index;
                    bestD = d;
                }
            }
            
            if (leftClick)
            {
                return new Input(){ Direction = mousePos - virus.Position, InteractWith = hoveredOver };
            }
            else
            {
                return default;
            }
        }
    }
    
    public struct Visuals
    {
        public Collection<VirusVisuals, Virus> Viruses;
        public Collection<CellVisuals, Cell> Cells;
    }
    
    public struct VirusVisuals : IDisposable
    {
        public const float TailRadius = 0.5f;
        public NativeList<float2> Tail;

        public void Dispose()
        {
            Tail.Dispose();
        }

        public void Render(ref CommandBuilder draw, in Virus virus, in float deltaTime)
        {
            draw.Arrowhead(float3(virus.Position, 0), float3(virus.Direction, 0), float3(0, 0, -1), Virus.Radius);
            
            // Update tail
            float2 last = virus.Position;
            for (int i = 0; i < Tail.Length; i++)
            {
                draw.Line(float3(last,0), float3(Tail[i],0));
                last = Tail[i];
            }
        }
        
        public void Update(in State state, in Virus virus, in float deltaTime)
        {
            // Handle tail collisions: Push them out of all colliders, then push them out of eachother
            
            // Update tail positions: They should each be TailRadius apart
            
        }
    }
    
    public struct CellVisuals : IDisposable
    {
        public void Dispose()
        {
        }
    }
}