using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace cell.game
{
    public struct State : IDisposable
    {
        public Collection<Virus, Virus> Viruses;
        public Collection<Cell, Cell> Cells;

        public unsafe State(uint rngSeed, int virusCount, int cellCount)
        {
            Random r = Random.CreateFromIndex(rngSeed);

            Viruses = new (virusCount, Allocator.Persistent);
            Cells = new (cellCount, Allocator.Persistent);

            {
                var it = Viruses.Iterate;
                while (++it)
                {
                    it.Value->Position = r.NextFloat2(math.float2(0, 0), math.float2(100, 100));
                }
            }

            {
                var it = Cells.Iterate;
                while (++it)
                {
                    it.Value->Position = r.NextFloat2(math.float2(0, 0), math.float2(100, 100));
                }
            }
        }

        public static implicit operator bool(State state) => state.Viruses.Data.IsCreated;

        public unsafe void InjectInput(Id<Virus> virusId, Input input)
        {
            Viruses[virusId]->Input = input;
        }

        public unsafe void InjectInput(Id<Cell> cellId, Input input)
        {
            Cells[cellId]->Input = input;
        }

        public unsafe void Update(float dt)
        {
            // Movement
            Update_VirusMovement(dt);
            Update_CellMovement(dt);

            // Collision
            Update_VirusCollision(dt);
            Update_CellCollision(dt);
        }

        private unsafe void Update_VirusCollision(float dt)
        {
            var it = Viruses.Iterate;
            while (++it)
            {
                ref var virus = ref *it.Value;
                var virusC = virus.Collider;

                // Push viruses out of cells
                var cellIt = Cells.Iterate;
                while (++cellIt)
                {
                    ref var cell = ref *cellIt.Value;

                    if (virus.Input.InteractWith)
                    {
                        if (virus.TargetCell)
                        {
                            // Do nothing
                        }
                        else
                        {
                            // Attempt Attach
                            if (virusC.Overlaps(cell.VirusLatchCollider))
                                virus.TargetCell = cellIt.Index;
                        }
                    }

                    var cellC = cell.Collider;
                    if (!virusC.Overlaps(cellC)) continue;

                    if (virus.TargetCell)
                    {
                        //if (virus.TargetCell == cellIt) continue;
                        //
                        //// Move the cell out of us (we're kind of like a cell collider)
                        //cellC.MoveOutOf(virusC, out float2 shift, out float2 reflectNormal);
                        //cell.Position += shift;
//
                        //var velDot = math.dot(cell.Velocity, reflectNormal);
                        //if (velDot < 0)
                        //    cell.Velocity = math.reflect(cell.Velocity, reflectNormal);
//
                        //cell.Velocity += shift;
                        //virus.Velocity += -shift;
                    }
                    else
                    {
                        virusC.MoveOutOf(cellC, out float2 shift, out float2 reflectNormal);
                        virus.Position += shift;

                        var velDot = math.dot(virus.Velocity, reflectNormal);
                        if (velDot < 0)
                            virus.Velocity = math.reflect(virus.Velocity, reflectNormal);

                        virus.Velocity += shift;
                        cell.Velocity += -shift;
                    }
                }

                // Push viruses out of viruses
                var virusIt = Viruses.Iterate;
                while (++virusIt)
                {
                    if (it == virusIt) continue;

                    ref var virus2 = ref *virusIt.Value;

                    var virus2C = virus2.Collider;
                    if (!virusC.Overlaps(virus2C)) continue;

                    virusC.MoveOutOf(virus2C, out float2 shift, out float2 reflectNormal);
                    virus.Position += shift;

                    var velDot = math.dot(virus.Velocity, reflectNormal);
                    if (velDot < 0)
                        virus.Velocity = math.reflect(virus.Velocity, reflectNormal);

                    virus.Velocity += shift;
                    virus2.Velocity += -shift;
                }
            }
        }

        private unsafe void Update_CellCollision(float dt)
        {
            var it = Cells.Iterate;
            while (++it)
            {
                ref var cell = ref *it.Value;
                var cellC = cell.Collider;

                // Push cells out of cells
                var cellIt = Cells.Iterate;
                while (++cellIt)
                {
                    if (it == cellIt) continue;


                    ref var cell2 = ref *cellIt.Value;
                    var cell2C = cell2.Collider;
                    if (!cellC.Overlaps(cell2C)) continue;

                    cellC.MoveOutOf(cell2C, out float2 shift, out float2 reflectNormal);
                    cell.Position += shift;

                    var velDot = math.dot(cell.Velocity, reflectNormal);
                    if (velDot < 0)
                        cell.Velocity = math.reflect(cell.Velocity, reflectNormal);

                    cell.Velocity += shift;
                    cell2.Velocity += -shift;
                }
            }
        }

        private unsafe void Update_VirusMovement(float dt)
        {
            var it = Viruses.Iterate;
            while (++it)
            {
                ref var virus = ref *it.Value;
                if (virus.InputCooldown <= 0 && math.any(virus.Input.Direction != 0))
                {
                    virus.Velocity += math.normalize(virus.Input.Direction);
                    virus.InputCooldown = 1;
                    
                    if (virus.TargetCell)
                    {
                        if (virus.Input.InteractWith != virus.TargetCell) // Detach
                        {
                            virus.TargetCell = Id<Cell>.Null;
                            virus.InputCooldown = 0.05f;
                        }
                        else
                        {
                            // Suck DNA from this motha
                            virus.DNA++;
                            virus.InputCooldown = 0.05f;
                        }
                    }
                    
                    virus.Input = default;
                }
                virus.InputCooldown = math.max(0, virus.InputCooldown - dt);
                
                if (virus.TargetCell)
                {
                    // Attach to cell
                    ref var cell = ref *Cells[virus.TargetCell];
                    var dir = math.normalizesafe(virus.Position - cell.Position, math.float2(1, 0));
                    virus.Position = math.lerp(virus.Position, cell.Position + (Cell.Radius + Virus.Radius) * dir, dt);
                    virus.Velocity = float2.zero;
                    virus.Direction = math.lerp(virus.Direction, -dir, dt);
                }
                else
                {
                    virus.Direction = math.normalizesafe(virus.Velocity, math.any(virus.Direction != 0) ? virus.Direction : math.float2(1, 0));

                    virus.Position += virus.Velocity * dt;
                    virus.Velocity += -virus.Velocity * dt;
                }
            }
        }

        private unsafe void Update_CellMovement(float dt)
        {
            var it = Cells.Iterate;
            while (++it)
            {
                ref var virus = ref *it.Value;
                if (virus.InputCooldown <= 0 && math.any(virus.Input.Direction != 0))
                {
                    virus.Velocity += math.normalize(virus.Input.Direction);
                    virus.Input = default;
                    virus.InputCooldown = 1;
                }

                virus.InputCooldown = math.max(0, virus.InputCooldown - dt);
                virus.Direction = math.normalizesafe(virus.Input.Direction, math.any(virus.Direction != 0) ? virus.Direction : math.float2(1, 0));

                virus.Position += virus.Velocity * dt;
                virus.Velocity += -virus.Velocity * dt;
            }
        }

        public void Dispose()
        {
            Viruses.Dispose();
            Cells.Dispose();
        }
    }

    public struct Input
    {
        public float2 Direction;
        public Id<Cell> InteractWith;
    }

    public struct Virus
    {
        public const float Radius = 1f;

        public float2 Position;
        public float2 Direction;
        public float2 Velocity;

        public Input Input;
        public float InputCooldown;

        public Id<Cell> TargetCell;
        public long DNA;

        public Circle Collider => new Circle() { Center = Position, Radius = Radius };
    }

    public struct Cell
    {
        public const float Radius = 10f;

        public float2 Position;
        public float2 Direction;
        public float2 Velocity;

        public Input Input;
        public float InputCooldown;

        public long DNA;

        public Circle Collider => new Circle() { Center = Position, Radius = Radius };
        public Circle VirusLatchCollider => new Circle() { Center = Position, Radius = Radius + Virus.Radius*2 };
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

    public struct Circle
    {
        public float2 Center;
        public float Radius;

        public Square BoundingBox => new Square() { Center = Center, Radius = Radius };

        public bool Overlaps(Circle other)
        {
            if (!BoundingBox.Overlaps(other.BoundingBox)) return false;

            return math.distancesq(Center, other.Center) < math.square(Radius + other.Radius);
        }

        public void MoveOutOf(Circle other, out float2 shift, out float2 reflectNormal)
        {
            var dif = math.normalizesafe(Center - other.Center, math.float2(1, 0));
            shift = other.Center + (Radius + other.Radius) * dif * 1.01f - Center;
            reflectNormal = dif;
        }
    }
}