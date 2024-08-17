using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Godot.NativeInterop;
using Array = System.Array;

public partial class JumpFlood : Node
{
	[Export] private Texture _seedTexture;
	[Export] private MeshInstance3D _outputMesh;

	[Export] private float _rainSize = 3.0f;
	[Export] private float _mouseSize = 5.0f;
	[Export] private Vector2I _texSize = new Vector2I(1024, 1024);
	[Export] private float _damp = 1.0f;

	private Random _rng;
	private Texture2Drd _texture;
	private int _textureNext = 0;
	private float _t;
	private float _maxT = 0.1f;
	private Vector4 _addWavePoint;
	
	public override void _Ready()
	{
		_rng = new Random();
		
		RenderingServer.CallOnRenderThread(new Callable(this, MethodName.InitialiseComputeCode));
		
		// Get our texture from our material so we set our RID.
		ShaderMaterial material = _outputMesh.GetActiveMaterial(0) as ShaderMaterial;
		_texture = material.GetShaderParameter("effect_texture").As<Texture2Drd>();
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (_texture != null) _texture.TextureRdRid = new Rid();
		RenderingServer.CallOnRenderThread(new Callable(this, MethodName.FreeComputeResources));
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		if (_t > _maxT)
		{
			_t = 0;
			_addWavePoint.X = _rng.Next() % _texSize.X;
			_addWavePoint.Y = _rng.Next() % _texSize.Y;
			_addWavePoint.Z = _rainSize;
		}
		
		// Increase our next texture index.
		_textureNext = (_textureNext + 1) % 3;
			
		// Update our texture to show our next result (we are about to create).
		// Note that `_initialize_compute_code` may not have run yet so the first
		// frame this my be an empty RID.
		if (_texture != null)
			_texture.TextureRdRid = _textureRds[_textureNext];
		
		// While our render_process may run on the render thread it will run before our texture
		// is used and thus our next_rd will be populated with our next result.
		// It's probably overkill to sent texture_size and damp as parameters as these are static
		// but we sent add_wave_point as it may be modified while process runs in parallel.
		Callable callable = Callable.From(() => RenderProcess(_textureNext, _addWavePoint, _texSize, _damp));
		RenderingServer.CallOnRenderThread(callable);
	}
	
	// ##############################################################################
	// Everything after this point is designed to run on our rendering thread.

	private RenderingDevice _rd;
	private Rid _shader;
	private Rid _pipeline;

	private Array<Rid> _textureRds = new Array<Rid>() {new Rid(), new Rid(), new Rid()};
	private Array<Rid> _textureSets = new Array<Rid>() {new Rid(), new Rid(), new Rid()};
	
	private void InitialiseComputeCode()
	{
		_rd = RenderingServer.GetRenderingDevice();
		
		// Create our shader.
		RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://water_plane/water_compute.glsl");
		RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
		_shader = _rd.ShaderCreateFromSpirV(shaderBytecode);
		_pipeline = _rd.ComputePipelineCreate(_shader);
		
		// Create our textures to manage our wave.
		RDTextureFormat tf = new RDTextureFormat();
		tf.Format = RenderingDevice.DataFormat.R32Sfloat;
		tf.TextureType = RenderingDevice.TextureType.Type2D;
		tf.Width = 1024;
		tf.Height = 1024;
		tf.Depth = 1;
		tf.ArrayLayers = 1;
		tf.Mipmaps = 1;
		tf.UsageBits = (
			RenderingDevice.TextureUsageBits.SamplingBit |
			RenderingDevice.TextureUsageBits.ColorAttachmentBit |
			RenderingDevice.TextureUsageBits.StorageBit |
			RenderingDevice.TextureUsageBits.CanUpdateBit |
			RenderingDevice.TextureUsageBits.CanCopyToBit
		);

		for (int i = 0; i < 3; i++)
		{
			// Create our texture.
			_textureRds[i] = _rd.TextureCreate(tf, new RDTextureView(), new Array<byte[]>());

			// Make sure our textures are cleared.
			_rd.TextureClear(_textureRds[i], new Color(0, 0, 0, 0), 0, 1, 0, 1);

			// Now create our uniform set so we can use these textures in our shader.
			_textureSets[i] = CreateUniformSet(_textureRds[i]);
		}
	}

	private void RenderProcess(int nextTexture, Vector4 wavePoint, Vector2I texSize, float pDamp)
	{
		// We don't have structures (yet) so we need to build our push constant "the hard way"...
		List<float> pushConstant = new List<float>();
		pushConstant.Add(wavePoint.X);
		pushConstant.Add(wavePoint.Y);
		pushConstant.Add(wavePoint.Z);
		pushConstant.Add(wavePoint.W);
		pushConstant.Add(texSize.X);
		pushConstant.Add(texSize.Y);
		pushConstant.Add(pDamp);
		pushConstant.Add(0.0f);

		byte[] byteBuffer = new byte[pushConstant.Count * 4];
		Buffer.BlockCopy(pushConstant.ToArray(), 0, byteBuffer, 0, pushConstant.Count * 4);

		int xGroups = (texSize.X - 1) / 8 + 1;
		int yGroups = (texSize.Y - 1) / 8 + 1;

		Rid nextSet = _textureSets[nextTexture];
		Rid currentSet = _textureSets[(nextTexture - 1 + 3) % 3];
		Rid prevSet = _textureSets[(nextTexture - 2 + 3) % 3];
		
		long computeList = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(computeList, _pipeline);
		_rd.ComputeListBindUniformSet(computeList, currentSet, 0);
		_rd.ComputeListBindUniformSet(computeList, prevSet, 1);
		_rd.ComputeListBindUniformSet(computeList, nextSet, 2);
		_rd.ComputeListSetPushConstant(computeList, byteBuffer, (uint) (pushConstant.Count * 4));
		_rd.ComputeListDispatch(computeList, (uint) xGroups, (uint) yGroups, 1);
		_rd.ComputeListEnd();
	}
	
	private Rid CreateUniformSet(Rid textureRd)
	{
		RDUniform uniform = new RDUniform();
		uniform.UniformType = RenderingDevice.UniformType.Image;
		uniform.Binding = 0;
		uniform.AddId(textureRd);
		return _rd.UniformSetCreate(new Array<RDUniform>() { uniform }, _shader, 0);
	}

	private void FreeComputeResources()
	{
		for (int i = 0; i < 3; i++)
		{
			_rd.FreeRid(_textureRds[i]);
		}
		_rd.FreeRid(_shader);
	}
}
