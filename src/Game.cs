using Godot;
using System;

public partial class Game : Node
{
	public override void _Ready()
	{
		base._Ready();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (Input.IsActionJustReleased("fullscreen"))
		{
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			}
			else
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			}
		}
	}
}
