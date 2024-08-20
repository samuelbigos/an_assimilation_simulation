using Godot;
using System;
using ImGuiNET;

public partial class Game : Singleton<Game>
{
	public bool Paused => _paused;
	
	private bool _paused = true;
	private bool _gameOver = false;

	public static bool SimulationMode = false;

	public override void _Ready()
	{
		base._Ready();
		
		if (SimulationMode) Unpause();
	}

	public void Unpause()
	{
		_paused = false;
	}

	public void TriggerWin()
	{
		if (_gameOver) return;
		_gameOver = true;
		DebugImGui.Instance.RegisterWindow("win", "Assimilation Complete!", _ImGuiWin);
		DebugImGui.Instance.SetCustomWindowEnabled("win", true);
	}

	public void TriggerLose()
	{
		if (_gameOver) return;
		_gameOver = true;
		DebugImGui.Instance.RegisterWindow("lose", "You've been assimilated.", _ImGuiLose);
		DebugImGui.Instance.SetCustomWindowEnabled("lose", true);
	}
	
	private void _ImGuiWin()
	{
		ImGui.Text("#############################################");
		ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();
		if (ImGui.Button("Restart"))
		{
			GetTree().ReloadCurrentScene();
		}
	}
	
	private void _ImGuiLose()
	{
		ImGui.Text("#############################################");
		ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();ImGui.Indent();
		if (ImGui.Button("Retry"))
		{
			GetTree().ReloadCurrentScene();
		}
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

		if (Input.IsActionJustReleased("pause"))
		{
			// if (_paused) Engine.TimeScale = 1.0f;
			// else Engine.TimeScale = 0.0f;
			_paused = !_paused;
		}
	}
}
