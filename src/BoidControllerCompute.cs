using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public partial class BoidControllerCompute : Node
{
	[Export] private Terrain _terrain;
	[Export] private float _boidMaxSpeed = 1.0f;
	[Export] private float _boidMaxForce = 0.1f;
	[Export] private float _boidDefaultRadius = 2.0f;
	[Export] private float _boidSeparationRadius = 2.0f;
	[Export] private float _boidCohesionRadius = 10.0f;
	[Export] private float _boidAlignmentRadius = 15.0f;
	[Export] private float _boidSdfAvoidDistance = 10.0f;

	private struct Boid
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float Radius;
	}
	private List<Boid> _boids = new List<Boid>();

	private bool _readyToProcess = false;
	private Random _rng = new Random();
	
	// Boid buffers
	private const int MAX_BOIDS = 4096;
	
	// Compute
	private RenderingDevice _rd;
	private Vector2I _workgroupSize = new Vector2I(32, 32);
	private Rid _shader;
	private Rid _pipeline;

	private Rid _positionBuffer;
	private Rid _positionSet;
	private Rid _velocityBuffer;
	private Rid _velocitySet;
	private Rid _radiiBuffer;
	private Rid _radiiSet;
	private Rid _debugOutBuffer;
	private Rid _debugOutSet;
	private Rid _distanceFieldSet;
	
	public override void _Ready()
	{
		base._Ready();
		
		_terrain.DistanceFieldCreated += DistanceFieldCreated;
	}
	
	private void DistanceFieldCreated()
	{
		_boids.Add(new Boid()
		{
			Position = Vector2.Zero,
			Velocity = Vector2.Zero,
			Radius = 5.0f,
		});
		
		_rd = RenderingServer.GetRenderingDevice();
		{
			RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://assets/boid_compute.glsl");
			RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
			_shader = _rd.ShaderCreateFromSpirV(shaderBytecode);
			_pipeline = _rd.ComputePipelineCreate(_shader);
		}

		byte[] positions = new byte[MAX_BOIDS * 8];
		Span<Vector2> positionsSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(positions, 0, MAX_BOIDS * 8));
		byte[] velocities = new byte[MAX_BOIDS * 8];
		Span<Vector2> velocitiesSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(velocities, 0, MAX_BOIDS * 8));
		byte[] radii = new byte[MAX_BOIDS * 4];
		Span<float> radiiSpan = MemoryMarshal.Cast<byte, float>(new Span<byte>(radii, 0, MAX_BOIDS * 4));
		
		float sqrtSize = Mathf.Sqrt(MAX_BOIDS);
		float spacing = _boidDefaultRadius * 2.5f;
		List<Vector2I> safePositions = new List<Vector2I>(_terrain.SafePositions);
		for (int i = 0; i < MAX_BOIDS; i++)
		{
			// positionsSpan[i] = new Vector2(
			// 	spacing * (i % (int)sqrtSize - sqrtSize / 2), 
			// 	spacing * (i / (int)sqrtSize - sqrtSize / 2));
			
			velocitiesSpan[i] = new Vector2(1.0f, 1.0f) * 0.01f;

			int safeIndex = _rng.Next() % safePositions.Count;
			// Give a little bump in a random direction to prevent two boids spawning at the same position and exploding the compute shader (I think).
			positionsSpan[i] = safePositions[safeIndex] + new Vector2((float) _rng.NextDouble(), (float) _rng.NextDouble()) * 0.1f;
			//safePositions.RemoveAt(safeIndex);
			
			//velocitiesSpan[i] = new Vector2((float) _rng.NextDouble(), (float) _rng.NextDouble()) * 0.1f;
			
			radiiSpan[i] = _boidDefaultRadius;
		}

		// positionsSpan[0] = new Vector2(-20.0f, 50.0f);
		// positionsSpan[1] = new Vector2(20.0f, 50.0f);
		// velocitiesSpan[0] = new Vector2(0.1f, 0.0f);
		// velocitiesSpan[1] = new Vector2(-0.1f, 0.0f);
		
		// Layouts
		_positionBuffer = _rd.StorageBufferCreate(MAX_BOIDS * 8, positions);
		_velocityBuffer = _rd.StorageBufferCreate(MAX_BOIDS * 8, velocities);
		_radiiBuffer = _rd.StorageBufferCreate(MAX_BOIDS * 4, radii);
		_debugOutBuffer = _rd.StorageBufferCreate(MAX_BOIDS * 16);
		_rd.BufferClear(_debugOutBuffer, 0, MAX_BOIDS * 16);
		_positionSet = CreateUniform(_positionBuffer, _shader, RenderingDevice.UniformType.StorageBuffer, 0);
		_velocitySet = CreateUniform(_velocityBuffer, _shader, RenderingDevice.UniformType.StorageBuffer, 1);
		_radiiSet = CreateUniform(_radiiBuffer, _shader, RenderingDevice.UniformType.StorageBuffer, 2);
		_debugOutSet = CreateUniform(_debugOutBuffer, _shader, RenderingDevice.UniformType.StorageBuffer, 3);
		
		RDUniform uniform = new RDUniform();
		uniform.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
		uniform.Binding = 0;
		RDSamplerState sampler = new RDSamplerState();
		sampler.MagFilter = RenderingDevice.SamplerFilter.Linear;
		uniform.AddId(_rd.SamplerCreate(sampler));
		uniform.AddId(_terrain.DistanceField);
		_distanceFieldSet = _rd.UniformSetCreate([uniform], _shader, 4);
		Utils.Assert(_distanceFieldSet.IsValid, "Failed to create uniform.");
		
		_readyToProcess = true;
	}

	private int steps = 0;
	public override void _Process(double delta)
	{
		base._Process(delta);

		if (!_readyToProcess) return;
		//if (steps > 0) return;
		
		ExecuteCompute(_pipeline);
		
		// Copy boid buffers to CPU for rendering.
		byte[] positions = _rd.BufferGetData(_positionBuffer, 0, MAX_BOIDS * 8);
		Span<Vector2> positionsSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(positions, 0, positions.Length));
		
		byte[] velocities = _rd.BufferGetData(_velocityBuffer, 0, MAX_BOIDS * 8);
		Span<Vector2> velocitiesSpan = MemoryMarshal.Cast<byte, Vector2>(new Span<byte>(velocities, 0, velocities.Length));
		
		byte[] radii = _rd.BufferGetData(_radiiBuffer, 0, MAX_BOIDS * 4);
		Span<float> radiiSpan = MemoryMarshal.Cast<byte, float>(new Span<byte>(radii, 0, radii.Length));
		
		byte[] debugOut = _rd.BufferGetData(_debugOutBuffer, 0, MAX_BOIDS * 16);
		Span<Vector4> debugOutSpan = MemoryMarshal.Cast<byte, Vector4>(new Span<byte>(debugOut, 0, debugOut.Length));

		for (int i = 0; i < positionsSpan.Length; i++)
		{
			//GD.Print($"Boid #{i}: Position: {positionsSpan[i]} Velocity: {velocitiesSpan[i]}");
			//GD.Print($"Debug: {debugOutSpan[i]}");
			
			// Draw boid.
			Vector3 boidPos = positionsSpan[i].To3D();
			Vector3 forward = velocitiesSpan[i].To3D().Normalized();
			Vector3 right = new(forward.Z, 0.0f, -forward.X);
			Color col = Colors.Blue;
			float size = radiiSpan[i];
			Vector3 p0 = boidPos + forward * -size * 0.33f + right * size * 0.5f;
			Vector3 p1 = boidPos + forward * size * 0.66f;
			Vector3 p2 = boidPos + forward * -size * 0.33f - right * size * 0.5f;
			DebugDraw.Circle(boidPos, 32, size, Colors.DarkGray);
			DebugDraw.Line(p0, p1, col);
			DebugDraw.Line(p1, p2, col);
			DebugDraw.Line(p2, p0, col);
		}

		steps++;
	}
	
	private void ExecuteCompute(Rid pipeline)
	{
		long computeList = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(computeList, pipeline);
		
		_rd.ComputeListBindUniformSet(computeList, _positionSet, 0);
		_rd.ComputeListBindUniformSet(computeList, _velocitySet, 1);
		_rd.ComputeListBindUniformSet(computeList, _radiiSet, 2);
		_rd.ComputeListBindUniformSet(computeList, _debugOutSet, 3);
		_rd.ComputeListBindUniformSet(computeList, _distanceFieldSet, 4);
		
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
			_boidSdfAvoidDistance
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
