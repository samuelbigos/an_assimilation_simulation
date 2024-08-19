using Godot;
using System;

public partial class Camera : Camera3D
{
	[Export] private float _zoomSpeed = 0.01f;
	
	private bool _dragging;
	private Vector2 _dragStartPos;
	private Vector3 _camStartPos;
	private float _initialCamSize;
	
	public override void _Ready()
	{
		_initialCamSize = Size;
	}

	public override void _Process(double delta)
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
