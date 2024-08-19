using Godot;
using System;
using System.Diagnostics;
using Array = Godot.Collections.Array;

public partial class SteeringManager
{
    private bool _draw = true;
    private bool _drawSeparation = true;
    private bool _drawArrive = false;
    private bool _drawSteering = true;
    private bool _drawVelocity = true;
    private bool _drawVision = false;
    private bool _drawAvoidance = false;
    private bool _drawWander = false;
    private bool _drawFlowFields = false;
    
    private void DebugDrawSteering()
    {
        // target
        DebugDraw.Circle(_targetPosition.To3D(), 32, 5f, Colors.Green);
        DebugDraw.Circle(_targetPosition.To3D(), 32, 3f, Colors.Green);
        DebugDraw.Circle(_targetPosition.To3D(), 32, 1f, Colors.Green);
        
        // boids
        Span<Boid> boids = _boidPool.AsSpan();
        foreach (ref readonly Boid boid in boids)
        {
            // body
            Vector3 boidPos = boid.Position.To3D().ToGodot();
            Vector3 forward = boid.Heading.To3D().ToGodot();
            Vector3 right = new(forward.Z, 0.0f, -forward.X);
            Color col = boid.Alignment == 0 ? Colors.Blue : Colors.Red;
            col = Color.Color8(230, 230, 230);
            float size = boid.Radius;
            Vector3 p0 = boidPos + forward * -size * 0.33f + right * size * 0.5f;
            Vector3 p1 = boidPos + forward * size * 0.66f;
            Vector3 p2 = boidPos + forward * -size * 0.33f - right * size * 0.5f;
            DebugDraw.Line(p0, p1, col);
            DebugDraw.Line(p1, p2, col);
            DebugDraw.Line(p2, p0, col);

            // separation radius
            if (_drawSeparation)
            {
                DebugDraw.Circle(boidPos, 32, boid.Radius, Colors.DarkGray);
            }

            // boid velocity/force
            if (_drawVelocity)
            {
                DebugDraw.Line(boidPos, boidPos + boid.Velocity.To3D().ToGodot() * Engine.MaxFps, Colors.Red);
            }

            // arrive radius and deadzone
            if (_drawArrive)
            {
                DebugDraw.Circle(boidPos, 32, boid.ArriveRadius, Colors.DarkGreen);
            }

            if (_drawSteering)
            {
                DebugDraw.Line(boidPos, boidPos + boid.Steering.To3D().ToGodot() * Engine.MaxFps * 10.0f,
                    Colors.Purple);
            }

            // boid avoidance
#if !FINAL
            if (boid.Intersection.Intersect && _drawAvoidance)
            {
                //Line(_st, boid.Position, boid.Position + forward * boid.LookAhead * boid.Speed, Colors.Black, ref v);
                Vector3 surface = boid.Intersection.SurfacePoint.To3D().ToGodot();
                DebugDraw.Circle(surface, 8, 1.0f, Colors.Black);
                DebugDraw.Line(surface, surface + boid.Intersection.SurfaceNormal.To3D().ToGodot() * 10.0f, Colors.Black);
            }
#endif

            // view range
            if (_drawVision)
            {
                Color visCol = Color.Color8(30, 30, 30);
                DebugDraw.CircleArc(boidPos, 32, boid.ViewRange, boid.ViewAngle, boid.Heading.ToGodot(), visCol);
            }

            // wander
            if (_drawWander && boid.HasBehaviour(Behaviours.Wander))
            {
                Vector3 circlePos = boidPos + boid.Heading.To3D().ToGodot() * boid.WanderCircleDist;

                float angle = -boid.Heading.AngleTo(System.Numerics.Vector2.UnitX) + boid.WanderAngle;
                Vector3 displacement = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                displacement = displacement.Normalized() * boid.WanderCircleRadius;

                DebugDraw.Circle(circlePos, 32, boid.WanderCircleRadius, Colors.SlateGray);
                DebugDraw.Line(circlePos, circlePos + displacement, Colors.DarkGray);
            }
        }
    }
}
