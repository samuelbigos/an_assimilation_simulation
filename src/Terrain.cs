using Godot;
using System;

public partial class Terrain : Node
{
	[Export] private Sprite3D _sprite;
	[Export] private NoiseTexture2D _noiseTex;
	[Export] private Resource _jumpFloodComputerShader;
	[Export] private Resource _easyComputeScript;

	private Vector2I _workgroupSize = new Vector2I(32, 32);
	private GDScript _gdScript;
	private GodotObject _compute;
	private Vector2I _textureSize;
	
	public override void _Ready()
	{
		_gdScript = GD.Load<GDScript>(_easyComputeScript.ResourcePath);
		_compute = (GodotObject)_gdScript.New();
	}

	public override void _Process(double delta)
	{
		_textureSize = new Vector2I(_noiseTex.Width, _noiseTex.Height);
		
		_compute.Call("load_shader", "fill", _jumpFloodComputerShader.ResourcePath);

		Image image = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		ImageTexture texture = ImageTexture.CreateFromImage(image);
		_compute.Call("register_texture", "some_texture", 0, _textureSize.X, _textureSize.Y, image.GetData(), (int)RenderingDevice.DataFormat.R8G8B8A8Unorm);

		float[] fillColor = [1.0f, 1.0f, 0.0f, 1.0f];
		byte[] fillColorByte = new byte[fillColor.Length * 4];
		Buffer.BlockCopy(fillColor, 0, fillColorByte, 0, fillColor.Length * 4);
		_compute.Call("register_storage_buffer", "fill_color", 1, 0, fillColorByte);

		Image noiseTexImage = _noiseTex.GetImage();
		Image noiseImage = Image.CreateEmpty(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8);
		for (int x = 0; x < _textureSize.X; x++) {
			for (int y = 0; y < _textureSize.Y; y++) {
				Color col = noiseTexImage.GetPixel(x, y);
				noiseImage.SetPixel(x, y, col);
			}
		}

		_compute.Call("register_texture", "noise_texture", 2, _textureSize.X, _textureSize.Y, noiseImage.GetData(), (int) RenderingDevice.DataFormat.R8G8B8A8Unorm);
		
		Vector2I workgroupCount = new Vector2I(_textureSize.X / _workgroupSize.X, _textureSize.Y / _workgroupSize.Y);
		_compute.Call("execute", "fill", workgroupCount.X, workgroupCount.Y, 1);
		_compute.Call("sync");

		byte[] imageData = _compute.Call("fetch_texture", "some_texture").AsByteArray();
		image = Image.CreateFromData(_textureSize.X, _textureSize.Y, false, Image.Format.Rgba8, imageData);
		texture.Update(image);

		_sprite.Texture = texture;
	}
}
