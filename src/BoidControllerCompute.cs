using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public partial class BoidControllerCompute : Node
{
	[Export] private Terrain _terrain;
	[Export] private GameCamera _cam;

	[Export] private int _playerStartBoids = 32;
	
	[Export] private float _boidMaxSpeed = 1.0f;
	[Export] private float _boidMaxForce = 0.1f;
	[Export] private float _boidDefaultRadius = 2.0f;
	[Export] private float _boidSeparationRadius = 2.0f;
	[Export] private float _boidCohesionRadius = 10.0f;
	[Export] private float _boidAlignmentRadius = 15.0f;
	[Export] private float _boidSdfAvoidDistance = 10.0f;
	[Export] private float _boidTeamInfluenceRadius = 15.0f;
	[Export] private float _boidMouseInfluenceRadius = 30.0f;

	private bool _readyToProcess = false;
	private Random _rng = new Random();
	
	// Boid buffers
	private const int MAX_BOIDS = 2048;
	
	// Compute
	private RenderingDevice _rd;
	private Vector2I _workgroupSize = new Vector2I(32, 32);
	private Rid _shader;
	private Rid _pipeline;

	enum Buffers
	{
		Position,
		Velocity,
		Radius,
		Team,
		Neighbours,
		DebugOut,
		COUNT
	}
	private int[] _bufferSizes = new[]
	{
		8,
		8,
		4,
		4,
		4,
		16
	};
	private Rid[] _buffers = new Rid[(int) Buffers.COUNT];
	private Rid[] _bufferSets = new Rid[(int) Buffers.COUNT];
	private Rid _distanceFieldSet;
	
	public override void _Ready()
	{
		base._Ready();
		
		_terrain.DistanceFieldCreated += DistanceFieldCreated;
	}
	
	private void DistanceFieldCreated()
	{
		_rd = RenderingServer.GetRenderingDevice();
		{
			RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://assets/boid_compute.glsl");
			RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
			_shader = _rd.ShaderCreateFromSpirV(shaderBytecode);
			_pipeline = _rd.ComputePipelineCreate(_shader);
		}

		// Create the compute buffers.
		List<byte[]> buffers = new List<byte[]>();
		for (int i = 0; i < (int) Buffers.COUNT; i++) {
			buffers.Add(new byte[MAX_BOIDS * _bufferSizes[i]]);
		}
		
		// Populate the compute buffers.
		Span<Vector2> positionsSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(buffers[(int) Buffers.Position], 0, MAX_BOIDS * _bufferSizes[(int) Buffers.Position]));
		Span<Vector2> velocitiesSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(buffers[(int) Buffers.Velocity], 0, MAX_BOIDS * _bufferSizes[(int) Buffers.Velocity]));
		Span<float> radiiSpan = MemoryMarshal.Cast<byte, float>(new Span<byte>(buffers[(int) Buffers.Radius], 0, MAX_BOIDS * _bufferSizes[(int) Buffers.Radius]));
		Span<int> teamSpan = MemoryMarshal.Cast<byte, int>(new Span<byte>(buffers[(int) Buffers.Team], 0, MAX_BOIDS * _bufferSizes[(int) Buffers.Team]));
		
		for (int i = 0; i < MAX_BOIDS - _playerStartBoids; i++)
		{
			int safeIndex = _rng.Next() % _terrain.EnemySpawn.Count;
			positionsSpan[i] = _terrain.EnemySpawn[safeIndex];
			// Give a little bump in a random direction to prevent two boids spawning at the same position and exploding the compute shader (I think).
			positionsSpan[i] += new Vector2((float) _rng.NextDouble() - 0.5f, (float) _rng.NextDouble() - 0.5f) * 0.1f;
			velocitiesSpan[i] = new Vector2((float) _rng.NextDouble() - 0.5f, (float) _rng.NextDouble() - 0.5f) * 0.1f;
			radiiSpan[i] = _boidDefaultRadius;
			teamSpan[i] = 1;
		}

		for (int i = MAX_BOIDS - _playerStartBoids; i < MAX_BOIDS; i++)
		{
			int safeIndex = _rng.Next() % _terrain.PlayerSpawn.Count;
			positionsSpan[i] = _terrain.PlayerSpawn[safeIndex];
			// Give a little bump in a random direction to prevent two boids spawning at the same position and exploding the compute shader (I think).
			positionsSpan[i] += new Vector2((float) _rng.NextDouble() - 0.5f, (float) _rng.NextDouble() - 0.5f) * 0.1f;
			velocitiesSpan[i] = new Vector2((float) _rng.NextDouble() - 0.5f, (float) _rng.NextDouble() - 0.5f) * 0.1f;
			radiiSpan[i] = _boidDefaultRadius;
			teamSpan[i] = 0;
		}

		// Layout the compute buffers.
		for (int i = 0; i < (int) Buffers.COUNT; i++) {
			_buffers[i] = _rd.StorageBufferCreate((uint) (MAX_BOIDS * _bufferSizes[i]), buffers[i]);
			_bufferSets[i] = CreateUniform(_buffers[i], _shader, RenderingDevice.UniformType.StorageBuffer, (uint)i);
		}

		// Clear the buffers we didn't populate.
		_rd.BufferClear(_buffers[(int) Buffers.DebugOut], 0, (uint) _bufferSizes[(int) Buffers.DebugOut]);
		_rd.BufferClear(_buffers[(int) Buffers.Neighbours], 0, (uint) _bufferSizes[(int) Buffers.Neighbours]);
		
		// Distance Field Layout
		RDUniform uniform = new RDUniform();
		uniform.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
		uniform.Binding = 0;
		RDSamplerState sampler = new RDSamplerState();
		sampler.MagFilter = RenderingDevice.SamplerFilter.Linear;
		uniform.AddId(_rd.SamplerCreate(sampler));
		uniform.AddId(_terrain.DistanceField);
		_distanceFieldSet = _rd.UniformSetCreate([uniform], _shader, (uint) Buffers.COUNT);
		Utils.Assert(_distanceFieldSet.IsValid, "Failed to create uniform.");
		
		_readyToProcess = true;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (!_readyToProcess) return;
		
		ExecuteCompute(_pipeline);
		
		// Copy boid buffers to CPU for rendering.
		byte[] positions = _rd.BufferGetData(_buffers[(int) Buffers.Position], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.Position]));
		Span<Vector2> positionsSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(positions, 0, positions.Length));
		
		byte[] velocities = _rd.BufferGetData(_buffers[(int) Buffers.Velocity], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.Velocity]));
		Span<Vector2> velocitiesSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(velocities, 0, velocities.Length));
		
		byte[] radii = _rd.BufferGetData(_buffers[(int) Buffers.Radius], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.Radius]));
		Span<float> radiiSpan = MemoryMarshal.Cast<byte, float>(new Span<byte>(radii, 0, radii.Length));
		
		byte[] team = _rd.BufferGetData(_buffers[(int) Buffers.Team], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.Team]));
		Span<int> teamSpan = MemoryMarshal.Cast<byte, int>(new Span<byte>(team, 0, team.Length));
		
		byte[] neighbours = _rd.BufferGetData(_buffers[(int) Buffers.Neighbours], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.Neighbours]));
		Span<float> neightboursSpan = MemoryMarshal.Cast<byte, float>(new Span<byte>(neighbours, 0, team.Length));
		
		byte[] debugOut = _rd.BufferGetData(_buffers[(int) Buffers.DebugOut], 0, (uint) (MAX_BOIDS * _bufferSizes[(int) Buffers.DebugOut]));
		Span<Vector4> debugOutSpan = MemoryMarshal.Cast<byte, Vector4>(new Span<byte>(debugOut, 0, debugOut.Length));

		Vector2 min = new Vector2(9999.0f, 9999.0f);
		Vector2 max = new Vector2(-9999.0f, -9999.0f);
		for (int i = 0; i < positionsSpan.Length; i++)
		{
			//GD.Print($"Boid #{i}: Position: {positionsSpan[i]} Velocity: {velocitiesSpan[i]}");
			//GD.Print($"Debug: {debugOutSpan[i]}");

			if (teamSpan[i] == 0)
			{
				min.X = Mathf.Min(min.X, positionsSpan[i].X);
				min.Y = Mathf.Min(min.Y, positionsSpan[i].Y);
				max.X = Mathf.Max(max.X, positionsSpan[i].X);
				max.Y = Mathf.Max(max.Y, positionsSpan[i].Y);
			}
			
			// Draw boid.
			Vector3 boidPos = positionsSpan[i].To3D();
			Vector3 forward = velocitiesSpan[i].To3D().Normalized();
			Vector3 right = new(forward.Z, 0.0f, -forward.X);
			Color col = Colors.Green;
			// if (neightboursSpan[i] > 4) col = Colors.Yellow;
			// if (neightboursSpan[i] > 8) col = Colors.Orange;
			// if (neightboursSpan[i] > 12) col = Colors.Red;
			if (teamSpan[i] == 1) col = Colors.Red;
			float size = radiiSpan[i];
			Vector3 p0 = boidPos + forward * -size * 0.33f + right * size * 0.5f;
			Vector3 p1 = boidPos + forward * size * 0.66f;
			Vector3 p2 = boidPos + forward * -size * 0.33f - right * size * 0.5f;
			DebugDraw.Circle(boidPos, 32, size * 0.9f, Colors.DarkGray);
			DebugDraw.Line(p0, p1, col);
			DebugDraw.Line(p1, p2, col);
			DebugDraw.Line(p2, p0, col);
		}
		
		// Update the camera to contain all our boids.
		_cam.SetMinMax(min, max);
	}
	
	private void ExecuteCompute(Rid pipeline)
	{
		long computeList = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(computeList, pipeline);

		for (uint i = 0; i < (int) Buffers.COUNT; i++)
		{
			_rd.ComputeListBindUniformSet(computeList, _bufferSets[i], i);
		}
		_rd.ComputeListBindUniformSet(computeList, _distanceFieldSet, (uint) Buffers.COUNT);
		
		Vector3 mouseWorld = _cam.ProjectPosition(GetViewport().GetMousePosition(), 0.0f);
		
		// Ordering must be the same as defined in the compute shader.
		List<float> constants = [ MAX_BOIDS, 
			_terrain.Size.X, 
			_terrain.Size.Y, 
			_terrain.SDFDistMod,
			_boidMaxSpeed,
			_boidMaxForce,
			_boidSeparationRadius,
			_boidCohesionRadius,
			_boidAlignmentRadius,
			_boidSdfAvoidDistance,
			_boidTeamInfluenceRadius,
			Input.IsActionPressed("seek") ? 1.0f : 0.0f,
			mouseWorld.X,
			mouseWorld.Z,
			_boidMouseInfluenceRadius
		];
		
		// Padding
		int alignment = 4;
		int paddedSize = Mathf.CeilToInt((float)constants.Count / alignment) * alignment;
		while (constants.Count < paddedSize) constants.Add(0);
		
		byte[] constantsByte = new byte[constants.Count * 4];
		Buffer.BlockCopy(constants.ToArray(), 0, constantsByte, 0, constantsByte.Length);
		_rd.ComputeListSetPushConstant(computeList, constantsByte, (uint) constantsByte.Length);
		
		int groups = Mathf.CeilToInt(MAX_BOIDS / 1024.0f);
		_rd.ComputeListDispatch(computeList, (uint) groups, 1, 1);
		_rd.ComputeListEnd();
		_rd.Submit();
		_rd.Sync();
	}
	
	private Rid CreateUniform(Rid rid, Rid shader, RenderingDevice.UniformType type, uint set)
	{
		RDUniform uniform = new RDUniform();
		uniform.UniformType = type;
		uniform.Binding = 0;
		uniform.AddId(rid);
		Rid uniformRid = _rd.UniformSetCreate([uniform], shader, set);
		Utils.Assert(uniformRid.IsValid, "Failed to create uniform.");
		return uniformRid;
	}
}
