#define ENABLE_CHECKS

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;

public class ArrayXD<T> where T : struct
{
    int _dims;
    int _x, _y, _z, _w;
    int _sY, _sZ, _sW;

    private T[] _internal;

    public int InternalSize => _internal.Length;

    public int X => _x;
    public int Y => _y;
    public int Z => _z;
    public int W => _w;

    public ArrayXD(int x)
    {
        _dims = 1;
        _x = x;
        _y = 0;
        _z = 0;
        _w = 0;
        _sY = 0;
        _sZ = 0;
        _sW = 0;
        _internal = new T[_x];
    }
    
    public ArrayXD(int x, int y)
    {
        _dims = 2;
        _x = x;
        _y = y;
        _z = 0;
        _w = 0;
        _sY = y;
        _sZ = 0;
        _sW = 0;
        _internal = new T[_x * _y];
    }

    public ArrayXD(int x, int y, int z)
    {
        _dims = 3;
        _x = x;
        _y = y;
        _z = z;
        _w = 0;
        _sY = y * z;
        _sZ = z;
        _sW = 0;
        _internal = new T[_x * _y * _z];
    }

    public ArrayXD(int x, int y, int z, int w)
    {
        _dims = 4;
        _x = x;
        _y = y;
        _z = z;
        _w = w;
        _sY = y * z * w;
        _sZ = z * w;
        _sW = w;
        _internal = new T[_x * _y * _z * _w];
    }

    public T this[int x]
    {
        get => _internal[x];
        set => _internal[x] = value;
    }

    public T this[int x, int y]
    {
        get => _internal[ToNonDimensional(x, y)];
        set => _internal[ToNonDimensional(x, y)] = value;
    }

    public T this[int x, int y, int z]
    {
        get => _internal[ToNonDimensional(x, y, z)];
        set => _internal[ToNonDimensional(x, y, z)] = value;
    }

    public T this[int x, int y, int z, int w]
    {
        get => _internal[ToNonDimensional(x, y, z, w)];
        set => _internal[ToNonDimensional(x, y, z, w)] = value;
    }

    void Check(int dims, int x, int y)
    {
#if ENABLE_CHECKS
        if (dims != _dims) GD.Print($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
        if (x >= _x) GD.Print($"Out of range (access {x} of {_x}");
        if (y >= _y) GD.Print($"Out of range (access {y} of {_y}");
#endif
    }
    [Conditional("UNITY_EDITOR")]
    void Check(int dims, int x, int y, int z)
    {
#if ENABLE_CHECKS
        if (dims != _dims) GD.Print($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
        if (x >= _x) GD.Print($"Out of range (access {x} of {_x}");
        if (y >= _y) GD.Print($"Out of range (access {y} of {_y}");
        if (z >= _z) GD.Print($"Out of range (access {z} of {_z}");
#endif
    }
    [Conditional("UNITY_EDITOR")]
    void Check(int dims, int x, int y, int z, int w)
    {
#if ENABLE_CHECKS
        if (dims != _dims) GD.Print($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
        if (x >= _x) GD.Print($"Out of range (access {x} of {_x}");
        if (y >= _y) GD.Print($"Out of range (access {y} of {_y}");
        if (z >= _z) GD.Print($"Out of range (access {z} of {_z}");
        if (w >= _w) GD.Print($"Out of range (access {w} of {_w}");
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToNonDimensional(int x, int y) 
    { 
        Check(2, x, y); 
        return x * _sY + y; 
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToNonDimensional(int x, int y, int z) 
    { 
        Check(3, x, y, z);  
        return x * _sY + y * _sZ + z; 
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToNonDimensional(int x, int y, int z, int w) 
    { 
        Check(4, x, y, z, w); 
        return x * _sY + y * _sZ + z * _sW + w; 
    }
}