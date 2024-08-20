using Godot;
using System;

public partial class GameCamera : Camera3D
{
	[Export] private Terrain _terrain;
	[Export] private float _zoomSpeed = 0.01f;

	public static GameCamera Instance;

	public enum CameraMode
	{
		Auto,
		Manual
	}
	
	private bool _dragging;
	private Vector2 _dragStartPos;
	private Vector3 _camStartPos;
	private float _initialCamSize;
	private Vector3 _initialPos;

	private CameraMode _mode = CameraMode.Auto;
	private Vector2 _targetPosition;
	private float _targetSize;
	private bool _firstFrame = true;
	
	public override void _Ready()
	{
		_firstFrame = true;
		_initialCamSize = Size;
		_initialPos = GlobalPosition;

		if (Game.SimulationMode)
		{
			SetCameraMode(CameraMode.Manual);
		}
	}

	public override void _EnterTree()
	{
		base._EnterTree();

		Instance = this;
	}

	public void SetCameraMode(CameraMode mode)
	{
		if (_mode == mode) return;
		_mode = mode;
		if (_mode == CameraMode.Manual)
		{
			Size = _terrain.Size.X * 0.75f;
			GlobalPosition = _initialPos;
		}
	}

	private void ExpandAabb(Vector2 point, ref Vector2 min, ref Vector2 max)
	{
		min.X = Mathf.Min(min.X, point.X);
		min.Y = Mathf.Min(min.Y, point.Y);
		max.X = Mathf.Max(max.X, point.X);
		max.Y = Mathf.Max(max.Y, point.Y);
	}

	public void SetMinMax(Vector2 min, Vector2 max)
	{
		Vector3 mouseWorld = ProjectPosition(GetViewport().GetMousePosition(), 0.0f);
		
		_targetPosition = (min + max) / 2.0f;
		_targetPosition = (_targetPosition + mouseWorld.To2D()) * 0.5f;
		float mouseDist = (_targetPosition + mouseWorld.To2D()).Length();
		_targetSize = Mathf.Clamp(Mathf.Max((max - min).X, (max - min).Y) + 64.0f, 128.0f, _terrain.Size.X * 0.75f);

		if (_firstFrame && _mode == CameraMode.Auto)
		{
			_firstFrame = false;
			Size = _targetSize;
			GlobalPosition = new Vector3(_targetPosition.X, _initialPos.Y, _targetPosition.Y);
		}
	}

	public override void _Process(double delta)
	{
		if (_mode == CameraMode.Auto)
		{
			float trackSpeed = 0.05f;
			Size = float.Lerp(Size, _targetSize, 1.0f - float.Pow(trackSpeed, (float) delta));
			GlobalPosition = new Vector3(float.Lerp(GlobalPosition.X, _targetPosition.X, 1.0f - float.Pow(trackSpeed, (float) delta)),
					_initialPos.Y,
					float.Lerp(GlobalPosition.Z, _targetPosition.Y, 1.0f - float.Pow(trackSpeed, (float) delta)));
		}
		else
		{
			if (Input.IsActionJustPressed("camera_drag"))
			{
				_dragging = true;
				_camStartPos = GlobalPosition;
				_dragStartPos = GetViewport().GetMousePosition();
			}
			
			if (Input.IsActionJustReleased("camera_drag"))
			{
				_dragging = false;
			}
			
			if (_dragging)
			{
				Vector2 mousePos = GetViewport().GetMousePosition();
				Vector2 mouseDelta = (_dragStartPos - mousePos);
				mouseDelta *= Size / _initialCamSize;
				SetGlobalPosition(_camStartPos + mouseDelta.To3D());
			}
			
			if (Input.IsActionJustPressed("camera_zoom_in"))
			{
				Size -= Size * _zoomSpeed;
			}
			if (Input.IsActionJustPressed("camera_zoom_out"))
			{
				Size += Size * _zoomSpeed;
			}
		}
	}
}
