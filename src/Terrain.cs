using Godot;
using System;

public partial class Terrain : Node
{
	[Export] private Sprite3D _sprite;
	[Export] private NoiseTexture2D _noiseTex;
	[Export] private Resource _easyComputeScript;

	private Vector2I _workgroupSize = new Vector2I(32, 32);
	private GDScript _gdScript;
	private Vector2I _textureSize;
	
	private GodotObject _voronoiSeedCompute;
	//private GodotObject _jumpFloodCompute;
	//private GodotObject _distanceFieldCompute;

	private Rid _voronoiSeedShader;
	
	private bool _generatedVoronoiSeed;
	private bool _generatedVoronoi;

	private Texture2D _voronoiSeedTexture;
	private Texture2D _voronoiTexture;

	private Rid[] _swapRds = new Rid[2];
	private Rid[] _swapSets = new Rid[2];
	
	public override void _Ready()
	{
		_textureSize = new Vector2I(_noiseTex.Width, _noiseTex.Height);
		_gdScript = GD.Load<GDScript>(_easyComputeScript.ResourcePath);
		
		_voronoiSeedCompute = (GodotObject)_gdScript.New();
		_voronoiSeedShader = _voronoiSeedCompute.Call("load_shader", "voronoi_seed", "res://assets/terrain/compute/voronoi_seed.glsl").AsRid();
		
		//_jumpFloodCompute = (GodotObject)_gdScript.New();
		_voronoiSeedCompute.Call("load_shader", "jump_flood", "res://assets/terrain/compute/jump_flood.glsl");
		
		//_distanceFieldCompute = (GodotObject)_gdScript.New();
		_voronoiSeedCompute.Call("load_shader", "distance_field", "res://assets/terrain/compute/distance_field.glsl");
	}

	private void CreateSwapTextures()
	{
		RDTextureFormat textureFormat = new RDTextureFormat();
		textureFormat.Format = RenderingDevice.DataFormat.R8G8B8A8Unorm;
		textureFormat.Width = (uint) _textureSize.X;
		textureFormat.Height = (uint) _textureSize.Y;
		textureFormat.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit |
		                           RenderingDevice.TextureUsageBits.ColorAttachmentBit |
		                           RenderingDevice.TextureUsageBits.StorageBit |
		                           RenderingDevice.TextureUsageBits.CanUpdateBit |
		                           RenderingDevice.TextureUsageBits.CanCopyToBit;

		RenderingDevice rd = RenderingServer.GetRenderingDevice();
		_swapRds[0] = rd.TextureCreate(textureFormat, new RDTextureView());
		_swapRds[1] = rd.TextureCreate(textureFormat, new RDTextureView());
		rd.TextureClear(_swapRds[0], Colors.Teal, 0, 1, 0, 1);
		rd.TextureClear(_swapRds[1], Colors.Teal, 0, 1, 0, 1);
		_swapSets[0] = CreateUniform(rd, _swapRds[0]);
		_swapSets[1] = CreateUniform(rd, _swapRds[1]);
	}

	private Rid CreateUniform(RenderingDevice rd, Rid rid)
	{
		RDUniform uniform = new RDUniform();
		uniform.UniformType = RenderingDevice.UniformType.Image;
		uniform.Binding = 0;
		uniform.AddId(rid);
		return rd.UniformSetCreate([uniform], _voronoiSeedShader, 0);
	}

	private void GenerateVoronoiSeed()
	{
		Image image = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		ImageTexture texture = ImageTexture.CreateFromImage(image);
		_voronoiSeedCompute.Call("register_texture", "voronoi_seed_texture", 0, _textureSize.X, _textureSize.Y, image.GetData(), (int)RenderingDevice.DataFormat.R8G8B8A8Unorm).As<Rid>();
		
		Image noiseTexImage = _noiseTex.GetImage();
		Image noiseImage = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		for (int x = 0; x < _textureSize.X; x++) {
			for (int y = 0; y < _textureSize.Y; y++) {
				Color col = noiseTexImage.GetPixel(x, y);
				noiseImage.SetPixel(x, y, col);
			}
		}
		
		_voronoiSeedCompute.Call("register_texture", "noise_texture", 2, _textureSize.X, _textureSize.Y, noiseImage.GetData(), (int) RenderingDevice.DataFormat.R8G8B8A8Unorm);
		
		Vector2I workgroupCount = new Vector2I(_textureSize.X / _workgroupSize.X, _textureSize.Y / _workgroupSize.Y);
		_voronoiSeedCompute.Call("execute", "voronoi_seed", workgroupCount.X, workgroupCount.Y, 1);
		_voronoiSeedCompute.Call("sync");
		
		byte[] imageData = _voronoiSeedCompute.Call("fetch_texture", "voronoi_seed_texture").AsByteArray();
		image = Image.CreateFromData(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8, imageData);
		texture.Update(image);
		
		_voronoiSeedTexture = texture;
		_sprite.Texture = _voronoiSeedTexture;
		
		_generatedVoronoiSeed = true;
	}

	private void GenerateVoronoi()
	{
		Image image = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		ImageTexture texture = ImageTexture.CreateFromImage(image);
		
		_voronoiSeedCompute.Call("register_texture", "jump_flood_texture", 0, _textureSize.X, _textureSize.Y, image.GetData(), (int)RenderingDevice.DataFormat.R8G8B8A8Unorm);

		float[] fillColor = [1.0f, 1.0f, 0.0f];
		byte[] fillColorByte = new byte[fillColor.Length * 4];
		Buffer.BlockCopy(fillColor, 0, fillColorByte, 0, fillColor.Length * 4);
		_voronoiSeedCompute.Call("register_storage_buffer", "fill_color", 1, 0, fillColorByte);

		Image noiseTexImage = _noiseTex.GetImage();
		Image noiseImage = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		for (int x = 0; x < _textureSize.X; x++) {
			for (int y = 0; y < _textureSize.Y; y++) {
				Color col = noiseTexImage.GetPixel(x, y);
				noiseImage.SetPixel(x, y, col);
			}
		}

		//_voronoiSeedCompute.Call("register_texture_rid", "voronoi_seed_texture", 2, _voronoiSeedTextureRid, (int)RenderingDevice.DataFormat.R8G8B8A8Unorm);
		
		Vector2I workgroupCount = new Vector2I(_textureSize.X / _workgroupSize.X, _textureSize.Y / _workgroupSize.Y);
		_voronoiSeedCompute.Call("execute", "jump_flood", workgroupCount.X, workgroupCount.Y, 1);
		_voronoiSeedCompute.Call("sync");

		byte[] imageData = _voronoiSeedCompute.Call("fetch_texture", "jump_flood_texture").AsByteArray();
		image = Image.CreateFromData(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8, imageData);
		texture.Update(image);
		
		_voronoiTexture = texture;
		_sprite.Texture = _voronoiTexture;
		
		_generatedVoronoi = true;
	}

	public override void _Process(double delta)
	{
		if (!_generatedVoronoiSeed)
		{
			GenerateVoronoiSeed();
		}

		if (!_generatedVoronoi)
		{
			//GenerateVoronoi();
		}
	}
}
