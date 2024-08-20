using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ImGuiNET;

public partial class DebugImGui : Saveable
{
    private Action DrawImGuiMenuBar = null;

    public static DebugImGui Instance;

    private struct RegisteredWindow
    {
        public string Id;
        public string Name;
        public Action Callback;
        public string Shortcut;
        public string ShortcutDisplay;
    }
    
    private float _timescale;
    private List<RegisteredWindow> _registeredWindows = new();
    private bool _hasMovedParent;
    
    private const float _windowAlpha = 0.75f;
    private ImGuiWindowFlags _windowFlags = ImGuiWindowFlags.AlwaysAutoResize;
    
    private Godot.Collections.Dictionary<string, Variant> _defaults = new()
    {
        { "performance", false },
        { "debug", false },
        { "gamesettings", false },
        { "help", true }
    };

    private bool ShowPerformance
    {
        get => _data["performance"].AsBool();
        set { _data["performance"] = value; SaveManager.DoSave();}
    }

    private bool ShowDebug
    {
        get => _data["debug"].AsBool();
        set { _data["debug"] = value; SaveManager.DoSave(); }
    }
    
    private bool ShowGameSettings
    {
        get => _data["gamesettings"].AsBool();
        set { _data["gamesettings"] = value; SaveManager.DoSave(); }
    }

    public override void _Ready()
    {
        base._Ready();
        
        RegisterWindow("performance", "Performance", _OnImGuiLayoutPerformance);
        RegisterWindow("debug", "Debug", _OnImGuiLayoutDebug);
        RegisterWindow("gamesettings", "Game Settings", _OnImGuiLayoutGameSettings);
        RegisterWindow("help", "Help", _OnImGuiLayoutHelp);

        if (!Game.SimulationMode)
            SetCustomWindowEnabled("help", true);
        
        _timescale = (float)Engine.TimeScale;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        Instance = null;
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        Instance = this;
    }

    public override void InitialiseSaveData()
    {
        Validate();
    }

    protected override void Validate()
    {
        foreach (string key in _defaults.Keys)
        {
            if (!_data.ContainsKey(key))
            {
                _data[key] = _defaults[key];
            }
        }

        foreach (RegisteredWindow window in _registeredWindows)
        {
            if (!_data.ContainsKey(window.Id))
            {
                _data[window.Id] = false;
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        
        _OnImGuiLayout();
    }
    
    public override void _Input(InputEvent evt)
    {
        foreach (RegisteredWindow window in _registeredWindows)
        {
            if (!InputMap.HasAction(window.Shortcut))
                continue;
            
            if (evt.IsActionPressed(window.Shortcut))
                SetCustomWindowEnabled(window.Id, !_data[window.Id].AsBool());
        }
    }

    public void SetCustomWindowEnabled(string Id, bool enabled)
    {
        _data[Id] = enabled;
        SaveManager.DoSave();
    }

    public void RegisterWindow(string id, string name, Action callback)
    {
        RegisteredWindow window = new() {Id = id, Name = name, Callback = callback, Shortcut = $"debug_show_{id}"};

        foreach (Variant action in InputMap.ActionGetEvents(window.Shortcut))
        {
            InputEventKey key = action.As<InputEventKey>();
            DebugUtils.Assert(key.CtrlPressed, $"RegisterWindow: {window.Id} action does not have Control modifier.");
            window.ShortcutDisplay += "CTRL+" + (char)key.Keycode;
        }
        
        _registeredWindows.Add(window);

        if (!_data.ContainsKey(id))
            _data[id] = false;
    }
    
    public void UnRegisterWindow(string id, Action callback)
    {
        int index = -1;
        for (int i = 0; i < _registeredWindows.Count; i++)
        {
            RegisteredWindow window = _registeredWindows[i];
            if (window.Id == id)
            {
                index = i;
                break;
            }
        }
        if (index != -1)
            _registeredWindows.RemoveAt(index);
    }

    private void _OnImGuiLayoutPerformance()
    {
        ImGui.Text($"FPS: {Performance.GetMonitor(Performance.Monitor.TimeFps):F0}");
        
        ImGui.Text(" ### Processing");
        ImGui.Text($"TimeProcess: {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0f:F0}ms");
        ImGui.Text($"ObjectCount: {Performance.GetMonitor(Performance.Monitor.ObjectCount):F0}");
        ImGui.Text($"ObjectNodeCount: {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0}");
        ImGui.Text($"ObjectResourceCount: {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):F0}");
        ImGui.Text($"ObjectOrphanNodeCount: {Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount):F0}");

        ImGui.Text(" ### Rendering");
        ImGui.Text($"RenderTotalDrawCallsInFrame: {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0}");
        ImGui.Text($"RenderTotalObjectsInFrame: {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):F0}");
        ImGui.Text($"RenderTotalPrimitivesInFrame: {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):F0}");

        ImGui.Text(" ### Memory");
        ImGui.Text($"MemoryStatic: {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024.0f:F0}KiB");
        ImGui.Text($"MemoryMessageBufferMax: {Performance.GetMonitor(Performance.Monitor.MemoryMessageBufferMax) / 1024.0f:F0}KiB");

        ImGui.Text(" ### Physics");
        ImGui.Text($"Physics3DActiveObjects: {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects):F0}");
        ImGui.Text($"Physics2DActiveObjects: {Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects):F0}");
        ImGui.Text($"Physics3DIslandCount: {Performance.GetMonitor(Performance.Monitor.Physics3DIslandCount):F0}KiB");
        ImGui.Text($"Physics2DIslandCount: {Performance.GetMonitor(Performance.Monitor.Physics2DIslandCount):F0}KiB");
    }
    
    private void _OnImGuiLayoutDebug()
    {
        ImGui.Text("There's nothing here...");
    }

    private int _cameraMode;
    private Vector2I _terrainSize;
    
    private void _OnImGuiLayoutGameSettings()
    {
        bool changed = false;
        ImGui.Text("Camera Mode:");
        ImGui.RadioButton("Auto", ref _cameraMode, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Manual", ref _cameraMode, 1);
        ImGui.TextWrapped("When in manual camera mode, drag the camera by holding the right mouse button and zoom in/out with the mouse wheel.");
        ImGui.Text("---------------------------------------------------------------");
        GameCamera.Instance.SetCameraMode((GameCamera.CameraMode)_cameraMode);
    }
    
    private void _OnImGuiLayoutHelp()
    {
        ImGui.TextWrapped("In this game you influence the movements of a small group of critters, with the goal of assimilating all other critters on the map.");
        ImGui.TextWrapped("The game is currently paused, when you click the below button, the game will begin.");
        ImGui.Spacing();
        ImGui.Text("## Controls ##");
        ImGui.Indent();
        ImGui.Text("Left Click: Influence your nearby critters to move to this position.");
        ImGui.Text("Spacebar / P: Pause.");
        ImGui.Spacing();
        ImGui.Unindent();
        ImGui.TextWrapped("A critter will be assimilated if it is surrounded by more foes than friends (this applies to your critters as well!)");
        ImGui.Spacing();
        ImGui.TextWrapped(">> Your objective is to assimilate all critters on the map. <<");
        ImGui.Spacing();
        ImGui.TextWrapped("At the top of the screen you can enable/disable various pages, including 'game settings' which provides various options to alter gameplay.");
        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.Button("Begin"))
        {
            Game.Instance.Unpause();
            SetCustomWindowEnabled("help", false);
        }
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Text("---------------------------------------------------------------");
        ImGui.TextWrapped("Simulation mode disables game mechanics and randomises critter allegiances, so you can sit back and watch them potter about assimilating each other." +
                          "Simulation parameters and visualisations can be adjusted using the menus at the top of the screen.");
        ImGui.Spacing();
        ImGui.TextWrapped("(You can switch modes at any time with the menu in the top left.)");
        ImGui.Spacing();
        if (ImGui.Button("Enter Simulation Mode"))
        {
            Game.SimulationMode = true;
            GetTree().ReloadCurrentScene();
        }
    }

    private void _OnImGuiLayout()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("Menu"))
            {
                if (ImGui.MenuItem("Restart - Game Mode"))
                {
                    Game.SimulationMode = false;
                    GetTree().ReloadCurrentScene();
                }
                if (ImGui.MenuItem("Restart - Simulation Mode"))
                {
                    Game.SimulationMode = true;
                    GetTree().ReloadCurrentScene();
                }
                if (ImGui.MenuItem("Quit"))
                {
                    GetTree().Quit();
                }
                ImGui.EndMenu();
            }
            
            if (ImGui.BeginMenu("Windows"))
            {
                foreach (RegisteredWindow window in _registeredWindows)
                {
                    bool selected = _data[window.Id].AsBool();
                    if (ImGui.MenuItem($"{window.Name}", window.ShortcutDisplay, selected))
                    {
                        SetCustomWindowEnabled(window.Id, !selected);
                    }
                }
                ImGui.EndMenu();
            }
            
            DrawImGuiMenuBar?.Invoke();
            
            ImGui.EndMainMenuBar();
        }

        foreach (RegisteredWindow window in _registeredWindows)
        {
            if (_data[window.Id].AsBool())
            {
                ImGui.SetNextWindowBgAlpha(_windowAlpha);
                if (ImGui.Begin(window.Name, _windowFlags))
                {
                    window.Callback?.Invoke();
                }
            }
        }
    }
}
