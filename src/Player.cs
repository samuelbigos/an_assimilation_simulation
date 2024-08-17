using System;
using Godot;

public partial class Player : BoidBase
{
	[Export] private float _maxForce = 0.1f;
	[Export] private float _maxSpeed = 1.0f;
	
	private int _steeringId;
	
	protected ref SteeringManager.Boid SteeringBoid => ref SteeringManager.Instance.GetBoid<SteeringManager.Boid>(_steeringId);
	
	public override void _Ready()
	{
		Init(OnDestroy, new Vector2(0.0f, 0.0f), new Vector2(0.0f, 0.0f));
	}
	
	private void OnDestroy(BoidBase obj)
	{
		throw new NotImplementedException();
	}

	public override void _Process(double delta)
	{
		DoPlayerInput();
	}
	
	private void DoPlayerInput()
	{
		Vector2 forward = new(0.0f, -1.0f);
		Vector2 left = new(-1.0f, 0.0f);

		Vector2 dir = new(0.0f, 0.0f);
		if (Input.IsActionPressed("w")) dir += forward;
		if (Input.IsActionPressed("s")) dir += -forward;
		if (Input.IsActionPressed("a")) dir += left;
		if (Input.IsActionPressed("d")) dir += -left;

		ref SteeringManager.Boid boid = ref SteeringBoid;
        
		float boost = Input.IsActionPressed("boost") ? 2.0f : 1.0f;
		boid.MaxSpeed = _maxSpeed * boost;
		boid.MaxForce = _maxForce * boost;
        
		if (dir != new Vector2(0.0f, 0.0f))
		{
			dir = dir.Normalized();
			boid.DesiredVelocityOverride = dir.ToNumerics() * 5000.0f;
			SetSteeringBehaviourEnabled(SteeringManager.Behaviours.Stop, false);
			SetSteeringBehaviourEnabled(SteeringManager.Behaviours.DesiredVelocityOverride, true);
		}
		else
		{
			boid.DesiredVelocityOverride = System.Numerics.Vector2.Zero;
			SetSteeringBehaviourEnabled(SteeringManager.Behaviours.Stop, true);
			SetSteeringBehaviourEnabled(SteeringManager.Behaviours.DesiredVelocityOverride, false);
		}
	}
}
