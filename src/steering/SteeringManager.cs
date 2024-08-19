using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using ImGuiNET;
using static SteeringManager.Behaviours;
using Vector2 = System.Numerics.Vector2;

public partial class SteeringManager : Singleton<SteeringManager>
{
    [Export] public GameCamera _cam;
    
    public enum ObstacleShape
    {
        Circle,
    }
    
    public interface IPoolable
    {
        public abstract bool Empty();
        public abstract int GenerateId();
    }

    public class Boid : IPoolable
    {
        public int Id;
        public byte Alignment;
        public Vector2 Position; // TODO: Separate position.
        public Vector2 Velocity;
        public Vector2 DesiredVelocityOverride;
        public Vector2 Steering;
        public Vector2 Heading;
        public float Radius;
        public float Speed;
        public float ArriveRadius;
        public Vector2 Target;
        public int TargetIndex;
        public Vector2 TargetOffset;
        public float DesiredDistFromTargetMin;
        public float DesiredDistFromTargetMax;
        public Vector2 DesiredOffsetFromTarget;
        public int Behaviours;
        public float DesiredSpeed;
        public float MaxSpeed;
        public float MinSpeed;
        public float MaxForce;
        public float LookAhead;
        public float ViewRange;
        public float ViewAngle;
        public float WanderAngle;
        public float WanderCircleDist;
        public float WanderCircleRadius;
        public float WanderVariance;
        public bool Ignore;
        
#if !FINAL
        public Intersection Intersection;
#endif

        public bool HasBehaviour(Behaviours behaviour)
        {
            return (Behaviours & (1 << (int) behaviour)) > 0;
        }

        public Godot.Vector2 PositionG => Position.ToGodot();
        public Godot.Vector2 VelocityG => Velocity.ToGodot();
        
        public bool Empty() => Id == 0;
        public static int IdGen;
        public int GenerateId()
        {
            Id = IdGen++;
            return Id;
        }
    }
    
    public struct Obstacle : IPoolable
    {
        public int Id;
        public Vector2 Position;
        public ObstacleShape Shape3D;
        public float Size;
        
        public bool Empty() => Id == 0;
        public static int IdGen;
        public int GenerateId()
        {
            Id = IdGen++;
            return Id;
        }
    }

    public struct FlowField : IPoolable
    {
        public int Id;
        public FlowFieldResource Resource;
        public int TrackID;
        public Vector2 Position;
        public Vector2 Size;

        public bool HasPoint(Vector2 point)
        {
            Vector2 tl = Position - Size * 0.5f;
            if (point.X < tl.X)
                return false;
            if (point.Y < tl.Y)
                return false;
            if (point.X >= tl.X + Size.X)
                return false;
            if (point.Y >= tl.Y + Size.Y)
                return false;
            return true;
        }

        public bool Empty() => Id == 0;
        public static int IdGen;
        public int GenerateId()
        {
            Id = IdGen++;
            return Id;
        }
    }

    public struct Intersection
    {
        public bool Intersect;
        public float IntersectTime;
        public Vector2 SurfacePoint;
        public Vector2 SurfaceNormal;
    }

    private static readonly int MAX_BOIDS = 1000;
    private static readonly int MAX_OBSTACLES = 100;
    private static readonly int MAX_FLOWFIELDS = 100;
    
    private StructPool<Boid> _boidPool = new(MAX_BOIDS);
    private StructPool<Obstacle> _obstaclePool = new(MAX_OBSTACLES);
    private StructPool<FlowField> _flowFieldPool = new(MAX_FLOWFIELDS);

    private Dictionary<int, int> _boidIdToIndex = new();
    private Dictionary<int, int> _obstacleIdToIndex = new();
    private Dictionary<int, int> _flowFieldIdToIndex = new();

    private Vector2[] _boidPositions = new Vector2[MAX_BOIDS];
    private byte[] _boidAlignments = new byte[MAX_BOIDS];
    
    private static int _boidIdGen = 1; // IDs start at 1 because Boid struct initialises default to 0.
    private static int _obstacleIdGen = 1;
    private static int _flowFieldIdGen = 1;
    private static float[] _behaviourWeights;
    private static double _delta;
    private Godot.Vector2 _targetPosition;

    public override void _Ready()
    {
        base._Ready();

        Boid.IdGen = 0;

        _behaviourWeights = new float[(int) COUNT];
        _behaviourWeights[(int) DesiredVelocityOverride] = 1.0f; 
        _behaviourWeights[(int) Separation] = 2.0f;
        _behaviourWeights[(int) AvoidObstacles] = 2.0f;
        _behaviourWeights[(int) AvoidAllies] = 2.0f;
        _behaviourWeights[(int) AvoidEnemies] = 2.0f;
        _behaviourWeights[(int) MaintainSpeed] = 0.1f;
        _behaviourWeights[(int) Cohesion] = 0.1f;
        _behaviourWeights[(int) Alignment] = 0.1f;
        _behaviourWeights[(int) Arrive] = 1.0f;
        _behaviourWeights[(int) Pursuit] = 1.0f;
        _behaviourWeights[(int) Flee] = 1.0f;
        _behaviourWeights[(int) Wander] = 0.1f;
        _behaviourWeights[(int) FlowFieldFollow] = 1.0f;
        _behaviourWeights[(int) MaintainDistance] = 1.0f;
        _behaviourWeights[(int) MaintainOffset] = 1.0f;
        _behaviourWeights[(int) Stop] = 1.0f;
        // this is low so it provides a subtle nudge and doesn't override other behaviours.
        _behaviourWeights[(int) MaintainBroadside] = 0.1f;

        Engine.MaxFps = 60;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        _delta = delta;

        //if (Input.IsMouseButtonPressed(MouseButton.Right))
        {
            Vector3 mouseWorld = _cam.ProjectPosition(GetViewport().GetMousePosition(), 0.0f);
            _targetPosition = new Vector2(mouseWorld.X, mouseWorld.Z).ToGodot();
        }
        
        Span<Boid> boids = _boidPool.AsSpan();
        ReadOnlySpan<Obstacle> obstacles = _obstaclePool.AsSpan();
        Span<FlowField> flowFields = _flowFieldPool.AsSpan();
        
        for (int i = 0; i < boids.Length; i++)
        {
            ref Boid boid = ref boids[i];
            boid.Target = _targetPosition.ToNumerics();
            
            Vector2 totalForce = Vector2.Zero;

            for (Behaviours j = 0; j < COUNT; j++)
            {
                if (!boid.HasBehaviour(j))
                    continue;

                Vector2 force = CalculateSteeringForce(j, ref boid, i, boids, obstacles, flowFields, delta);

                if (float.IsNaN(force.X) || float.IsInfinity(force.X))
                {
                    Debug.Assert(!float.IsNaN(force.X) && !float.IsInfinity(force.X), "NaN in steering calculation!");
                }

                // Truncate by max force per unit time.
                // https://gamedev.stackexchange.com/questions/173223/framerate-dependant-steering-behaviour
                float totalForceLength = totalForce.Length();
                float forceLength = force.Length();
                float frameMaxForce = boid.MaxForce * 2.0f;
                if (totalForceLength + forceLength > frameMaxForce)
                {
                    force.Limit(frameMaxForce - totalForceLength);
                    totalForce += force;
                    break;
                }

                totalForce += force;
            }

            //totalForce = ApplyMinimumSpeed(boid, totalForce, boid.MinSpeed);
            boid.Steering = totalForce;

            boid.Velocity += boid.Steering;
            boid.Velocity.Limit(boid.MaxSpeed);

            // TODO: replace max speed with drag, so max speed is a derived value from drag and thrust.
            // float dragCoeff = 0.0001f;
            // Vector2 drag = -boid.Velocity.NormalizeSafe() * boid.Velocity.LengthSquared() * dragCoeff;
            //boid.Velocity *= boid.Velocity.LengthSquared();

            boid.Speed = boid.Velocity.Length();

            // Smooth heading to eliminate rapid heading changes on small velocity adjustments
            if (boid.Speed > boid.MaxSpeed * 0.025f)
            {
                const float smoothing = 0.9f;
                boid.Heading = Vector2.Normalize(boid.Velocity) * (1.0f - smoothing) + boid.Heading * smoothing;
                boid.Heading = Vector2.Normalize(boid.Heading);
            }

            boid.Position += boid.Velocity;
            
            if (float.IsNaN(boid.Position.X))
            {
                Debug.Assert(!float.IsNaN(boid.Position.X), "!float.IsNaN(boid.Position.X)");
            }
        }

        DebugDrawSteering();
    }
    
    private static Vector2 CalculateSteeringForce(Behaviours behaviour, ref Boid boid, int index, ReadOnlySpan<Boid> boids, 
        ReadOnlySpan<Obstacle> obstacles, ReadOnlySpan<FlowField> flowFields, double delta)
    {
        Vector2 force = Vector2.Zero;
        float influence;
        
        switch (behaviour)
        {
            case Cohesion:
                force += Steering_Cohesion(boid, boids);
                break;
            case Alignment:
                force += Steering_Align(boid, boids);
                break;
            case Separation:
                force += Steering_Separate(boid, boids, obstacles, delta);
                break;
            case Arrive:
                force += Steering_Arrive(boid, boid.Target);
                break;
            case Pursuit:
                force += Steering_Pursuit(boid);
                break;
            case Flee:
                force += Steering_Flee(boid);
                break;
            case AvoidAllies:
                force += Steering_AvoidAllies(ref boid, index, boids);
                break;
            case AvoidEnemies:
                force += Steering_AvoidEnemies(ref boid, index, boids);
                break;
            case AvoidObstacles:
                force += Steering_AvoidObstacles(ref boid, obstacles);
                break;
            case MaintainSpeed:
                force += Steering_MaintainSpeed(boid);
                break;
            case Wander:
                force += Steering_Wander(ref boid, delta);
                break;
            case MaintainDistance:
                force += Steering_MaintainDistance(boid);
                break;
            case MaintainOffset:
                force += Steering_MaintainOffset(boid);
                break;
            case Stop:
                force += Steering_Stop(boid);
                break;
            case MaintainBroadside:
                force += Steering_MaintainBroadside(boid);
                break;
            case DesiredVelocityOverride:
                force += Steering_DesiredVelocityOverride(boid);
                break;
            case COUNT:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return force.Limit(boid.MaxForce * _behaviourWeights[(int) behaviour]);
    }
    
    public bool RegisterBoid<T>(T obj, out int id) where T : Boid
    {
        id = default;
        id = obj.GenerateId();
        int index = _boidPool.Add(obj);
        _boidIdToIndex[id] = index;
        return true;
    }
    
    public bool HasObject<T>(int id) where T : IPoolable
    {
        return _boidIdToIndex.ContainsKey(id);
    }
    
    public ref Boid GetBoid<T>(int id) where T : Boid
    {
        DebugUtils.Assert(_boidIdToIndex.ContainsKey(id), "Object doesn't exist.");
        return ref _boidPool.AsSpan()[_boidIdToIndex[id]];
    }
    
    public override void _EnterTree()
    {
        base._EnterTree();
        DebugImGui.Instance.RegisterWindow("steering", "Steering Behaviours", _OnImGuiLayout);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        DebugImGui.Instance.UnRegisterWindow("steering", _OnImGuiLayout);
    }

    private void _OnImGuiLayout()
    {
        if (ImGui.CollapsingHeader("Modes"))
        {
        }
        
        if (ImGui.CollapsingHeader("Drawing"))
        {
            ImGui.Checkbox("Draw", ref _draw);
            ImGui.Checkbox("Draw Separation", ref _drawSeparation);
            ImGui.Checkbox("Draw Arrive", ref _drawArrive);
            ImGui.Checkbox("Draw Steering", ref _drawSteering);
            ImGui.Checkbox("Draw Velocity", ref _drawVelocity);
            ImGui.Checkbox("Draw Vision", ref _drawVision);
            ImGui.Checkbox("Draw Avoidance", ref _drawAvoidance);
            ImGui.Checkbox("Draw Wander", ref _drawWander);
            ImGui.Checkbox("Draw FlowFields", ref _drawFlowFields);
        }
    }
}
