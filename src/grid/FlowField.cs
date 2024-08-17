using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class FlowField : Node
{
	[Export] private Grid _grid;

	private Dictionary<Vector2I, Vector2> _field;
	
	public override void _Ready()
	{
		if (_grid != null)
		{
			_grid.OnGridChanged += OnGridChanged;
		}
		_field = new Dictionary<Vector2I, Vector2>();
	}
	
	private void OnGridChanged()
	{
		FloodFill(new Vector2I(0, 0));
		
		Vector2I size = _grid.Size;
		for (int x = 0; x < size.X; x++) {
			for (int y = 0; y < size.X; y++) {
				
				Vector2I gridPos = new Vector2I(x, y);
				if (!_field.TryGetValue(gridPos, out Vector2 dir))
					continue;
				if (dir.IsZeroApprox())
					continue;
				
				Color color = Colors.White;
				if (_grid.GridValueAt(new Vector2I(x, y)) == Grid.GridValues.Blocked)
					color = Colors.Red;
				if (gridPos == new Vector2I(0, 0))
					color = Colors.Pink;

				Vector2 p1 = _grid.GridToWorldCentre(new Vector2I(x, y));
				Vector2 p2 = p1 + dir.Normalized() * 0.5f;
				DebugDraw.Line(p1.To3D(), p2.To3D(), color);
			}
		}
	}

	// Flood fill
	private Vector2I[] _dirs =
	{
		new Vector2I(1, 0),
		new Vector2I(1, 1),
		new Vector2I(0, 1),
		new Vector2I(-1, 1),
		new Vector2I(-1, 0),
		new Vector2I(-1, -1),
		new Vector2I(0, -1),	
		new Vector2I(1, -1)
	};
	private PriorityQueue<Vector2I, float> _frontier = new PriorityQueue<Vector2I, float>();
	private Dictionary<Vector2I, float> _costs = new Dictionary<Vector2I, float>();
	private void FloodFill(Vector2I target)
	{
		_field.Clear();
		_field[target] = new Vector2(0.0f, 0.0f);
		_frontier.Clear();
		_frontier.Enqueue(target, 0.0f);
		_costs.Clear();
		_costs[target] = 0.0f;

		while (_frontier.Count != 0) {
			Vector2I current = _frontier.Dequeue();
			foreach (Vector2I dir in _dirs) {
				Vector2I pos = current + dir;
				if (OutOfBounds(pos))
					continue;
				if (_grid.GridValueAt(pos) == Grid.GridValues.Blocked)
					continue;

				float cost = _costs[current] + Mathf.Sqrt(Mathf.Abs(dir.X) + Mathf.Abs(dir.Y));
				if (_costs.ContainsKey(pos) && cost >= _costs[pos])
					continue;

				_costs[pos] = cost;
				_frontier.Enqueue(pos, cost);
				_field[pos] = new Vector2(-dir.X, -dir.Y);
			}
		}

		foreach (KeyValuePair<Vector2I, Vector2> value in _field)
		{
			Vector2 dir = value.Value;
			Vector2I gridPos = value.Key;
			if (dir.IsZeroApprox())
				return;
				
			Color color = Colors.White;
			if (_grid.GridValueAt(value.Key) == Grid.GridValues.Blocked)
				color = Colors.Red;
			if (gridPos == new Vector2I(0, 0))
				color = Colors.Pink;
				
			Vector2 p1 = _grid.GridToWorld(gridPos) + _grid.CellSizeHalf;
			Vector2 p2 = p1 + dir.Normalized() * 0.5f;
			DebugDraw.Line(p1.To3D(), p2.To3D(), color);
		}
	}

	private bool OutOfBounds(Vector2I gridPos)
	{
		return gridPos.X < 0 || gridPos.Y < 0 || 
		       gridPos.X > _grid.Size.X - 1 || gridPos.Y > _grid.Size.Y - 1;
	}
}
