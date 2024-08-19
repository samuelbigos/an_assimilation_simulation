using Godot;
using System;

public partial class GameCamera : Camera3D
{
	[Export] private float _zoomSpeed = 0.01f;
	
	private bool _dragging;
	private Vector2 _dragStartPos;
	private Vector3 _camStartPos;
	private float _initialCamSize;
	private float _initialY;

	private bool _isTracking;
	private Vector2 _targetPosition;
	private float _targetSize;
	
	public override void _Ready()
	{
		_initialCamSize = Size;
		_initialY = GlobalPosition.Y;
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
		_targetSize = Mathf.Clamp(Mathf.Max((max - min).X, (max - min).Y) + 64.0f, 128.0f, 1024.0f);
		
		if (!_isTracking)
		{
			Size = _targetSize;
			GlobalPosition = new Vector3(_targetPosition.X, _initialY, _targetPosition.Y);
		}
		_isTracking = true;
	}

	public override void _Process(double delta)
	{
		if (_isTracking)
		{
			float trackSpeed = 0.1f;
			Size = float.Lerp(Size, _targetSize, 1.0f - float.Pow(trackSpeed, (float) delta));
			GlobalPosition = new Vector3(float.Lerp(GlobalPosition.X, _targetPosition.X, 1.0f - float.Pow(trackSpeed, (float) delta)),
					_initialY,
					float.Lerp(GlobalPosition.Z, _targetPosition.Y, 1.0f - float.Pow(trackSpeed, (float) delta)));
		}
		
		// if (Input.IsActionJustPressed("camera_drag"))
		// {
		// 	_dragging = true;
		// 	_camStartPos = GlobalPosition;
		// 	_dragStartPos = GetViewport().GetMousePosition();
		// }
		//
		// if (Input.IsActionJustReleased("camera_drag"))
		// {
		// 	_dragging = false;
		// }
		//
		// if (_dragging)
		// {
		// 	Vector2 mousePos = GetViewport().GetMousePosition();
		// 	Vector2 mouseDelta = (_dragStartPos - mousePos);
		// 	mouseDelta *= Size / _initialCamSize;
		// 	SetGlobalPosition(_camStartPos + mouseDelta.To3D());
		// }
		//
		// if (Input.IsActionJustPressed("camera_zoom_in"))
		// {
		// 	Size -= Size * _zoomSpeed;
		// }
		// if (Input.IsActionJustPressed("camera_zoom_out"))
		// {
		// 	Size += Size * _zoomSpeed;
		// }
	}
}
