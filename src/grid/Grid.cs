using Godot;
using System;
using System.Collections.Generic;
using Godot.Collections;

[Tool]
public partial class Grid : Node
{
	[Export] private Rect2 rect;
	
	public enum GridValues
	{
		Empty,
		Blocked,
	}

	public Vector2I Size => new Vector2I((int) rect.Size.X, (int) rect.Size.Y);
	public Vector2 CellSize => new Vector2(1.0f, 1.0f);
	public Vector2 CellSizeHalf => CellSize * 0.5f;

	public Action OnGridChanged;
	
	private List<MeshInstance3D> _obstacles = new List<MeshInstance3D>();
	private int _prevChildrenCount = -1;
	private ArrayXD<GridValues> _gridValues;
	private bool _gridDirty = true;
	private AStar2D _aStar = new AStar2D();
	private double _dirtyTimer;

	public Vector2 GridToWorld(Vector2I gridPos)
	{
		return new Vector2(rect.Position.X + gridPos.X, rect.Position.Y + gridPos.Y);
	}
	
	public Vector2 GridToWorldCentre(Vector2I gridPos)
	{
		return new Vector2(rect.Position.X + gridPos.X, rect.Position.Y + gridPos.Y)
			+ CellSizeHalf;
	}

	public GridValues GridValueAt(Vector2I gridPos)
	{
		return _gridValues[gridPos.X, gridPos.Y];
	}
	
	private int GridToID(int x, int y)
	{
		return (int) (x + rect.Size.Y * y);
	}
	
	public Vector2[] EvaluatePath(Vector2 from, Vector2 to)
	{
		return _aStar.GetPointPath(GridToID((int)from.X, (int)from.Y), GridToID((int)to.X, (int)to.Y));
	}

	public override void _Ready()
	{
		base._Ready();
	}

	public override void _Process(double delta)
	{
		_dirtyTimer += delta;
		if (_dirtyTimer > 1.0f)
		{
			_dirtyTimer = 0.0f;
			_gridDirty = true;
		}
		
		Array<Node> children = GetChildren();
		if (children.Count != _prevChildrenCount)
		{
			_gridValues = new ArrayXD<GridValues>((int) rect.Size.X, (int) rect.Size.Y);
			_obstacles.Clear();
			foreach (Node child in children) {
				if (child is MeshInstance3D) 
				{
					_obstacles.Add(child as MeshInstance3D);
				}
			}
			_gridDirty = true;
		}

		if (_gridDirty)
		{
			//_aStar.Clear();
			for (int x = 0; x < rect.Size.X; x++) {
				for (int y = 0; y < rect.Size.Y; y++) {
					_gridValues[x, y] = GridValues.Empty;
					foreach (MeshInstance3D obstacle in _obstacles) 
					{
						Aabb aabb = obstacle.GetAabb();
						aabb.Position -= CellSize.To3D() / obstacle.Scale;
						aabb.Size += CellSize.To3D() / obstacle.Scale;
						
						if (aabb.HasPoint(obstacle.Transform.AffineInverse() * GridToWorld(new Vector2I(x, y)).To3D()))
							_gridValues[x, y] = GridValues.Blocked;
					}
					// if (_gridValues[x, y] == GridValues.Empty)
					// {
					// 	int id = GridToID(x, y);
					// 	_aStar.AddPoint(id, new Vector2(x, y));
					// 	if (x > 0)
					// 		_aStar.ConnectPoints(id, GridToID(x - 1, y));
					// 	if (y > 0)
					// 		_aStar.ConnectPoints(id, GridToID(x, y - 1));
					// }
				}
			}
			OnGridChanged?.Invoke();
			_gridDirty = false;
		}
		
		DebugDraw.Rect2(rect, Colors.Black);
		
		for (int x = 1; x < rect.Size.X; x++) {
			DebugDraw.Line(new Vector3(rect.Position.X + x, 0.0f, rect.Position.Y),
				new Vector3(rect.Position.X + x, 0.0f, rect.Position.Y + rect.Size.Y),
				Colors.LightGray);
		}
		
		for (int y = 1; y < rect.Size.Y; y++) {
			DebugDraw.Line(new Vector3(rect.Position.X, 0.0f, rect.Position.Y + y),
				new Vector3(rect.Position.X + rect.Size.X, 0.0f, rect.Position.Y + y),
				Colors.LightGray);
		}
	}
}
