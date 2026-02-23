using Silk.NET.OpenGL;

namespace DesktopEarth.Rendering;

public class GlobeMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _indexCount;

    public GlobeMesh(GL gl, int stacks = 64, int slices = 128)
    {
        _gl = gl;

        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI * i / stacks;
            float y = MathF.Cos(phi);
            float sinPhi = MathF.Sin(phi);

            for (int j = 0; j <= slices; j++)
            {
                float theta = 2.0f * MathF.PI * j / slices;

                float x = sinPhi * MathF.Cos(theta);
                float z = sinPhi * MathF.Sin(theta);

                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);

                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);

                float u = (float)j / slices;
                float v = 1.0f - (float)i / stacks;
                vertices.Add(u);
                vertices.Add(v);
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint topLeft = (uint)(i * (slices + 1) + j);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((i + 1) * (slices + 1) + j);
                uint bottomRight = bottomLeft + 1;

                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);

                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }

        _indexCount = (uint)indices.Count;

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var vertexArray = vertices.ToArray();
        unsafe
        {
            fixed (float* ptr = vertexArray)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexArray.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        var indexArray = indices.ToArray();
        unsafe
        {
            fixed (uint* ptr = indexArray)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indexArray.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        uint stride = 8 * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0); }

        _gl.EnableVertexAttribArray(1);
        unsafe { _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float))); }

        _gl.EnableVertexAttribArray(2);
        unsafe { _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float))); }

        _gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, null);
        }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}
