using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImGuiNET;

public partial class BoidControllerCompute : Node
{
	[Export] private Terrain _terrain;
	[Export] private GameCamera _cam;
	[Export] private Game _game;
	[Export] private AudioStreamPlayer3D _convertAllySfx;
	[Export] private AudioStreamPlayer3D _convertEnemySfx;
	
	[Export] private int _playerStartBoids = 32;
	
	[Export] private float _boidMaxSpeed = 1.0f;
	[Export] private float _boidMaxForce = 0.1f;
	[Export] private float _boidSeparationWeight = 0.3f;
	[Export] private float _boidCohesionWeight = 0.4f;
	[Export] private float _boidAlignmentWeight = 0.3f;
	[Export] private float _boidDefaultRadius = 2.0f;
	[Export] private float _boidSeparationRadius = 2.0f;
	[Export] private float _boidCohesionRadius = 10.0f;
	[Export] private float _boidAlignmentRadius = 15.0f;
	[Export] private float _boidSdfAvoidWeight = 1.0f;
	[Export] private float _boidSdfAvoidDistance = 10.0f;
	[Export] private float _boidTeamInfluenceRadius = 15.0f;
	[Export] private float _boidMouseInfluenceRadius = 30.0f;

	private bool _readyToProcess = false;
	private Random _rng = new Random();
	
	// Boid buffers
	private static int _startingBoidCount = 2048;
	private int MAX_BOIDS = 2048;
	
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

	private bool _assimilateAll0;
	private bool _assimilateAll1;
	private int _prevNumAllies;
	private float _mousePressTimer;
	
	public override void _Ready()
	{
		base._Ready();
		
		_terrain.DistanceFieldCreated += DistanceFieldCreated;
		
		DebugImGui.Instance.RegisterWindow("simulation_params", "Simulation Parameters", _ImGuiCritters);

		if (Game.SimulationMode)
		{
			_playerStartBoids = 0;
			DebugImGui.Instance.SetCustomWindowEnabled("simulation_params", true);
		}

		MAX_BOIDS = _startingBoidCount;
	}

	public override void _EnterTree()
	{
		base._EnterTree();
	}
	
	private void _ImGuiCritters()
	{
		if (ImGui.Button("Assimilate all to player."))
		{
			_assimilateAll0 = true;
		}
		ImGui.SameLine();
		if (ImGui.Button("Assimilate all to enemy."))
		{
			_assimilateAll1 = true;
		}
		
		ImGui.Text("---------------------------------------------------------------");
		
		if (ImGui.CollapsingHeader("Steering Behaviours"))
		{
			ImGui.SliderFloat("Max Speed", ref _boidMaxSpeed, 0.0f, 5.0f);
			ImGui.SliderFloat("Max Force", ref _boidMaxForce, 0.0f, 1.0f);
			ImGui.Spacing();
			ImGui.TextWrapped("These parameters determine how the swarm flocks together.");
			ImGui.SliderFloat("Separation Weight", ref _boidSeparationWeight, 0.0f, 1.0f);
			ImGui.SliderFloat("Cohesion Weight", ref _boidCohesionWeight, 0.0f, 1.0f);
			ImGui.SliderFloat("Alignment Weight", ref _boidAlignmentWeight, 0.0f, 1.0f);
			ImGui.TextWrapped("The range at which other boids impart an influence for the above behaviours.");
			ImGui.SliderFloat("Separation Radius", ref _boidSeparationRadius, 0.0f, 50.0f);
			ImGui.SliderFloat("Cohesion Radius", ref _boidCohesionRadius, 0.0f, 50.0f);
			ImGui.SliderFloat("Alignment Radius", ref _boidAlignmentRadius, 0.0f, 50.0f);
			
			ImGui.Spacing();
			ImGui.TextWrapped("Critters will avoid the terrain, these parameters determine how strongly and at what range they move away from it.");
			ImGui.SliderFloat("Terrain Avoid Weight", ref _boidSdfAvoidWeight, 0.0f, 5.0f);
			ImGui.SliderFloat("Terrain Avoid Range", ref _boidSdfAvoidDistance, 0.0f, 50.0f);
			
			ImGui.Spacing();
		}
		
		ImGui.Text("---------------------------------------------------------------");
		ImGui.Text("Increase at your own risk.");
		ImGui.SliderInt("Critter Count", ref _startingBoidCount, 1, 8192 * 2);
		if (ImGui.Button("Apply"))
		{
			GetTree().ReloadCurrentScene();
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (DebugImGui.Instance != null)
			DebugImGui.Instance.UnRegisterWindow("simulation_params", _ImGuiCritters);
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

			if (Game.SimulationMode)
			{
				teamSpan[i] = _rng.Next() % 2;
			}
			else
			{
				teamSpan[i] = 1;
			}
		}

		_prevNumAllies = _playerStartBoids;
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

	private bool _firstFrame = true;

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (!_readyToProcess) return;
		if (_game.Paused && !_firstFrame) return;
		_firstFrame = false;
		
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
		int playerBoids = 0;
		int enemyBoids = 0;
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
			Color col = new Color("#345644");
			if (teamSpan[i] == 1) col = new Color("#913636");
			float size = radiiSpan[i];
			Vector3 p0 = boidPos + forward * -size * 0.33f + right * size * 0.5f;
			Vector3 p1 = boidPos + forward * size * 0.66f;
			Vector3 p2 = boidPos + forward * -size * 0.33f - right * size * 0.5f;
			DebugDraw.Circle(boidPos, 8, size * 0.9f, col);
			DebugDraw.Line(p0, p1, col);
			DebugDraw.Line(p1, p2, col);
			DebugDraw.Line(p2, p0, col);

			if (teamSpan[i] == 0) playerBoids++;
			if (teamSpan[i] == 1) enemyBoids++;
		}
		if (!Game.SimulationMode && Game.AudioEnabled)
		{
			if (playerBoids > _prevNumAllies)
			{
				_convertAllySfx.Play();
			}
			else if(playerBoids < _prevNumAllies)
			{
				_convertEnemySfx.Play();
			}
		}
		_prevNumAllies = playerBoids;

		// Draw the mouse influence circles.
		if (!Game.SimulationMode)
		{
			if (Input.IsActionJustPressed("seek")) _mousePressTimer = 0.0f;
			if (Input.IsActionPressed("seek"))
			{
				_mousePressTimer += (float)delta;
				Vector3 mouseWorld = _cam.ProjectPosition(GetViewport().GetMousePosition(), 0.0f);
				mouseWorld.Y = 0.0f;
				float duration = 1.0f;
				int numCircles = 3;
				float maxRadius = _boidMouseInfluenceRadius * 0.125f;
				for (int i = 0; i < numCircles; i++)
				{
					float t = ((_mousePressTimer + (duration / numCircles) * i) % duration) / duration;
					float radius = (1.0f - Mathf.Pow(t, 1.5f)) * maxRadius;
					Color col1 = new Color("#345644");
					Color col2 = new Color("#ceb57b");
					DebugDraw.Circle(mouseWorld, 32, radius, col1.Lerp(col2, 1.0f - t));
				}
			}
		}
		
		// Update the camera to contain all our boids.
		_cam.SetMinMax(min, max);

		if (enemyBoids == 0)
		{
			Game.Instance.TriggerWin();
		}
		if (playerBoids == 0)
		{
			Game.Instance.TriggerLose();
		}
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
			_boidSeparationWeight,
			_boidSeparationRadius,
			_boidCohesionWeight,
			_boidCohesionRadius,
			_boidAlignmentWeight,
			_boidAlignmentRadius,
			_boidSdfAvoidWeight,
			_boidSdfAvoidDistance,
			_boidTeamInfluenceRadius,
			(!Game.SimulationMode && Input.IsActionPressed("seek")) ? 1.0f : 0.0f,
			mouseWorld.X,
			mouseWorld.Z,
			_boidMouseInfluenceRadius,
			_assimilateAll0 ? 1.0f : 0.0f,
			_assimilateAll1 ? 1.0f : 0.0f,
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
