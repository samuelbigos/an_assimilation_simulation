using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Godot.Collections;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;

public partial class BoidBase : Area3D
{
    public enum BoidAlignment
    {
        Ally,
        Enemy
    };

    public enum TargetType
    {
        None,
        Ally,
        Enemy,
        Position
    }

    protected enum State
    {
        Alive,
        Destroyed
    }

    private struct HitMessage
    {
        public float Damage;
        public Vector2 Position;
        public Vector2 Velocity;
        public BoidAlignment Alignment;
    }

    #region Export

    [Export(PropertyHint.Flags, "DesiredVelocityOverride,Separation,AvoidObstacles,AvoidAllies,AvoidEnemies,Flee,MaintainSpeed,Cohesion,Alignment,Arrive,Pursuit,Wander,FlowFieldFollow")] protected int _behaviours;
    [Export] protected float _steeringRadius = 5.0f;

    [Export] public float _mass = 1.0f;
    [Export] public float MaxVelocity = 500.0f;
    [Export] public float MinVelocity = 0.0f;
    [Export] public float MaxForce = 150.0f;
    [Export] public float FieldOfView = 360.0f;
    [Export] public bool Bank360 = false;
    [Export] public float BankingRate = 2.5f;
    [Export] public float BankingAmount = 2.5f;
    
    [Export] private float _hitVfxDuration = 1.0f;
    [Export] private float _flashVfxDuration = 1.0f / 30.0f;
    
    [Export] private int _damageVfxCount = 2;
    [Export] private Array<AudioStream> _hitSfx;
    [Export] protected PackedScene _pickupMaterialScene;
    
    [Export] protected MeshInstance3D _selectedIndicator;
    [Export] protected CollisionShape3D _shipCollider;
    [Export] protected CollisionShape3D _rbCollider;
    [Export] private AudioStreamPlayer2D _sfxOnDestroy;
    [Export] protected AudioStreamPlayer2D _sfxOnHit;
    
    #endregion

    #region Signals

    public Action<BoidBase> OnBoidDestroyed;

    #endregion

    #region Public

    public virtual BoidAlignment Alignment => BoidAlignment.Ally;
    public bool Destroyed => _state == State.Destroyed;
    public int SteeringId => _steeringId;
    public Vector2 Heading => _cachedHeading;
    public Vector2 Velocity => _cachedVelocity;
    
    public Vector2 GlobalPosition
    {
        get => new(GlobalTransform.Origin.X, GlobalTransform.Origin.Z);
        protected set
        {
            if (SteeringManager.Instance.HasObject<SteeringManager.Boid>(_steeringId))
            {
                ref SteeringManager.Boid boid = ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);
                boid.Position = value.ToNumerics();
                GlobalTransform = new Transform3D(new Basis(Vector3.Down, boid.Heading.AngleToY()), value.To3D());
            }
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _selectedIndicator.Visible = _selected;
        }
    }

    public TargetType CurrentTargetType => _targetType;
    public Vector2 TargetPos
    {
        get
        {
            switch (_targetType)
            {
                case TargetType.Ally:
                case TargetType.Enemy:
                    return _targetBoid.GlobalPosition;
                case TargetType.Position:
                    return _targetPos;
            }
            return GlobalPosition;
        }
    }

    #endregion

    #region Protected

    protected int _steeringId;
    protected TargetType _targetType = TargetType.None;
    protected BoidBase _targetBoid;
    protected Vector2 _targetPos;
    protected bool _acceptInput = true;
    protected Vector2 _cachedVelocity;
    protected Vector2 _cachedHeading;
    protected Vector2 _visualHeadingOverride;
    protected State _state;
    protected ShaderMaterial _meshMaterial;

    protected ref SteeringManager.Boid SteeringBoid => ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);

    #endregion

    #region Private

    private float _health;
    private double _hitFlashTimer;
    private List<HitMessage> _hitMessages = new();
    private List<GpuParticles3D> _damagedParticles = new();
    private List<GpuParticles3D> _hitParticles = new();
    private Vector2 _cachedLastHitDir;
    private float _cachedLastHitDamage;
    private Vector2 _cachedLastHitPos;
    private bool _selected;
    private Vector2 _smoothSteering;
    private int _sharedPropertiesId;
    private RigidBody3D _destroyedRb;
    private bool _beginHitGround;
    private bool _hasHitGround;
    private bool _hitGround;
    private double _hitGroundTimer;

    #endregion

    public virtual void Init(Action<BoidBase> onDestroy, Vector2 position, Vector2 velocity)
    {
        _state = State.Alive;
        OnBoidDestroyed += onDestroy;
        RegisterSteeringBoid(velocity);
        GlobalPosition = position;
    }

    public override void _Ready()
    {
        base._Ready();
        
        _health = 1;

        _sfxOnHit.Stream = _hitSfx[0];

        Connect("area_entered", new Callable(this, nameof(_OnBoidAreaEntered)));

        _rbCollider.Disabled = true;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        switch (_state)
        {
            case State.Alive:
                ProcessAlive(delta);
                break;
            case State.Destroyed:
                ProcessDestroyed(delta);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        // Hit VFX.
        if (_hitFlashTimer > 0.0f)
        {
            _meshMaterial.SetShaderParameter("u_hit_time", _hitVfxDuration - _hitFlashTimer);
            _hitFlashTimer -= delta;
            if (_hitFlashTimer <= 0.0f)
            {
                _meshMaterial.SetShaderParameter("u_hits", 0);
            }
        }
    }

    protected virtual void ProcessAlive(double delta)
    {
        Debug.Assert(SteeringManager.Instance.HasObject<SteeringManager.Boid>(_steeringId));
        ref SteeringManager.Boid steeringBoid = ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);

        _cachedVelocity = steeringBoid.Velocity.ToGodot();
        _cachedHeading = steeringBoid.Heading.ToGodot();

        if (_visualHeadingOverride != Vector2.Zero)
        {
            _cachedHeading = _visualHeadingOverride;
        }
        Basis basis = new Basis(Vector3.Up, _cachedHeading.AngleToY());

        // Banking.
        if (delta > 0.0f)
        {
            System.Numerics.Vector2 right = new(-_cachedHeading.Y, _cachedHeading.X);
            System.Numerics.Vector2 localSteering = Utils.LocaliseDirection(steeringBoid.Steering, _cachedHeading.ToNumerics(), right);
            localSteering /= (float)delta;
            localSteering /= 100.0f;
            _smoothSteering = _smoothSteering.Lerp(localSteering.ToGodot(), Mathf.Clamp((float)delta * BankingRate, 0.0f, 1.0f));
            float bankX = Mathf.Clamp(_smoothSteering.Dot(Vector2.Down) * BankingAmount, -Mathf.Pi * 0.25f, Mathf.Pi * 0.25f);
            float bankZ = Mathf.Clamp(_smoothSteering.Dot(Vector2.Right) * BankingAmount, -Mathf.Pi * 0.25f, Mathf.Pi * 0.25f);
            basis = basis.Rotated(basis.Z, bankZ);
            if (Bank360)
                basis = basis.Rotated(basis.X, bankX);
        }
        
        // Update position and cache velocity from steering boid.
        GlobalTransform = new Transform3D(basis, steeringBoid.PositionG.To3D());

        // Process hits.
        foreach (HitMessage hit in _hitMessages)
        {
            if (hit.Alignment != Alignment)
            {
                _OnHit(hit.Damage, hit.Velocity, hit.Position);
            }
        }
        _hitMessages.Clear();

        if (_health <= 0.0f)
        {
            _OnDestroy(_cachedLastHitDir, _cachedLastHitDamage);
            return;
        }
        _cachedLastHitDir = Vector2.Zero;
        _cachedLastHitDamage = 0.0f;
    }

    private void ProcessDestroyed(double delta)
    {
    }

    private void RegisterSteeringBoid(Vector2 velocity)
    {
        SteeringManager.Boid boid = new()
        {   
            Alignment = (byte)Alignment,
            Radius = _steeringRadius,
            Position = GlobalPosition.ToNumerics(),
            Velocity = velocity.ToNumerics(),
            Heading = System.Numerics.Vector2.UnitY,
            Target = System.Numerics.Vector2.Zero,
            TargetIndex = -1,
            Behaviours = _behaviours,
            MaxSpeed = MaxVelocity,
            MinSpeed = MinVelocity,
            MaxForce = MaxForce,
            ArriveRadius = 25.0f,
            DesiredSpeed = 0.0f,
            LookAhead = 1.0f,
            ViewRange = 50.0f,
            ViewAngle = 240.0f,
            WanderCircleDist = 25.0f,
            WanderCircleRadius = 5.0f,
            WanderVariance = 50.0f,
        };
        
        if (!SteeringManager.Instance.RegisterBoid(boid, out _steeringId))
        {
            QueueFree();
            return;
        }
        
        ResetSteeringBehaviours();
    }

    public void SendHitMessage(float damage, Vector2 vel, Vector2 pos, BoidAlignment alignment)
    {
        _hitMessages.Add(new HitMessage()
        {
            Damage = damage, Alignment = alignment, Position = pos, Velocity = vel
        });
    }

    protected virtual void _OnHit(float damage, Vector2 bulletVel, Vector2 pos)
    {
    }
    
    protected virtual void _OnDestroy(Vector2 hitDir, float hitStrength)
    {
    }

    protected void SetSteeringBehaviourEnabled(SteeringManager.Behaviours behaviour, bool enabled, float weight = -1.0f)
    {
        ref SteeringManager.Boid steeringBoid = ref SteeringBoid;
        if (enabled) {
            steeringBoid.Behaviours |= (1 << (int) behaviour);
        }
        else {
            steeringBoid.Behaviours &= ~ (1 << (int) behaviour);
        }
    }

    protected virtual void SetTarget(TargetType type, BoidBase boid = null, Vector2 pos = new Vector2(), Vector2 offset = new Vector2())
    {
        Debug.Assert(SteeringManager.Instance.HasObject<SteeringManager.Boid>(_steeringId));

        if (!_targetBoid.Null())
        {
            _targetBoid.OnBoidDestroyed -= _OnTargetBoidDestroyed;
        }
        
        _targetType = type;
        _targetBoid = boid;
        _targetPos = pos;
        
        ref SteeringManager.Boid steeringBoid = ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);
        switch (type)
        {
            case TargetType.Ally:
            case TargetType.Enemy:
                DebugUtils.Assert(boid != null, "boid != null");
                steeringBoid.TargetIndex = boid.SteeringId;
                steeringBoid.TargetOffset = offset.ToNumerics();
                _targetBoid.OnBoidDestroyed += _OnTargetBoidDestroyed;
                break;
            case TargetType.Position:
                steeringBoid.Target = pos.ToNumerics();
                steeringBoid.TargetOffset = offset.ToNumerics();
                steeringBoid.TargetIndex = -1;
                break;
            case TargetType.None:
                steeringBoid.TargetIndex = -1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
    
    protected virtual void ResetSteeringBehaviours()
    {
        ref SteeringManager.Boid steeringBoid = ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);
        steeringBoid.Behaviours = _behaviours;
    }

    protected virtual void _OnBoidAreaEntered(Area3D area)
    {
        if (!IsInstanceValid(area))
            return;
    }

    protected void _WhenTheBodyHitsTheFloor(Node body)
    {
        if (body.IsInGroup("ground"))
        {
            if (!_hasHitGround)
            {
                _hasHitGround = true;
                _beginHitGround = true;
            }

            _hitGround = true;
        }
    }   

    protected virtual void _OnTargetBoidDestroyed(BoidBase boid)
    {
    }
}