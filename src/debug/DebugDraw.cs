using System;
using System.Diagnostics;
using Godot;
using Array = Godot.Collections.Array;

public partial class DebugDraw : Singleton<DebugDraw>
{
    private static int v;
    private static int i;
    private static Vector3[] _vertList = new Vector3[1024*1024];
    private static Color[] _colList = new Color[1024*1024];
    private static int[] _indexList = new int[1024*1024*2];
    private static MeshInstance3D _debugMesh;
    
    public override void _Ready()
    {
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        _debugMesh = null;
    }

    public override void _Process(double delta)
    {
        if (_debugMesh == null)
        {
            _debugMesh = new MeshInstance3D();
            StandardMaterial3D mat = new();
            mat.VertexColorUseAsAlbedo = true;
            mat.VertexColorIsSrgb = true;
            mat.NoDepthTest = true;
            _debugMesh.MaterialOverride = mat;
            AddChild(_debugMesh);
        }
        
        if (v == 0)
            return;
        
        Debug.Assert(v < _vertList.Length, "v < _vertList.Length");
        Debug.Assert(v < _colList.Length, "v < _colList.Length");
        Debug.Assert(i < _indexList.Length, "i < _indexList.Length");

        Span<Vector3> verts = _vertList.AsSpan(0, v);
        Span<Color> colours = _colList.AsSpan(0, v);
        Span<int> indices = _indexList.AsSpan(0, i);

        Array arrays = new();
        arrays.Resize((int) Mesh.ArrayType.Max);
        arrays[(int) Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int) Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int) Mesh.ArrayType.Index] = indices.ToArray();

        ArrayMesh mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        _debugMesh.Mesh = mesh;
        
        v = 0;
        i = 0;
    }
    
    public static void Line(Vector3 p1, Vector3 p2, Color col)
    {
        _colList[v] = col;
        _vertList[v] = p1;
        _indexList[i++] = v++;
        _colList[v] = col;
        _vertList[v] = p2;
        _indexList[i++] = v++;
    }
    
    public static void Circle(Vector3 pos, int segments, float radius, Color col)
    {
        for (int s = 0; s < segments; s++)
        {
            _colList[v + s] = col;
            float rad = Mathf.Pi * 2.0f * ((float) s / segments);
            Vector3 vert = pos + new Vector3(Mathf.Sin(rad), 0.0f, Mathf.Cos(rad)) * radius;
            _vertList[v + s] = vert;
            _indexList[i++] = v + s;
            _indexList[i++] = v + (s + 1) % segments;
        }

        v += segments;
    }
    
    public static void CircleArc(Vector3 pos, int segments, float radius, float arcDeg, Vector2 heading, Color col)
    {
        if (arcDeg >= 360.0f)
        {
            Circle(pos, segments, radius, col);
            return;
        }
        
        float segmentArc = Mathf.DegToRad(arcDeg / (segments - 1));
        float headingAngle = heading.AngleTo(new Vector2(0.0f, -1.0f)) - Mathf.DegToRad(arcDeg * 0.5f);
        _colList[v] = col;
        _vertList[v] = pos;
        v++;
        for (int s = 0; s < segments; s++)
        {
            _colList[v + s] = col;
            float rad = headingAngle + segmentArc * s;
            Vector3 vert = pos + new Vector3(Mathf.Sin(rad), 0.0f, Mathf.Cos(rad)) * radius;
            _vertList[v + s] = vert;
            _indexList[i++] = (v - 1 + (segments + s - 1) % segments);
            _indexList[i++] = (v - 1 + (segments + s) % segments);
        }
        v += segments;
    }

    public static void Rect2(Rect2 rect, Color col)
    {
        Vector3 p1 = rect.Position.To3D();
        Vector3 p2 = rect.Position.To3D() + new Vector3(rect.Size.X, 0.0f, 0.0f);
        Vector3 p3 = rect.Position.To3D() + new Vector3(rect.Size.X, 0.0f, rect.Size.Y);
        Vector3 p4 = rect.Position.To3D() + new Vector3(0.0f, 0.0f, rect.Size.Y);
        Line(p1, p2, col);
        Line(p2, p3, col);
        Line(p3, p4, col);
        Line(p4, p1, col);
    }
}
