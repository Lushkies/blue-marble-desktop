using Silk.NET.OpenGL;
using System.Numerics;

namespace DesktopEarth;

public class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetProgramInfoLog(_handle);
            throw new Exception($"Shader program link error: {info}");
        }

        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Use() => _gl.UseProgram(_handle);

    public void SetUniform(string name, int value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, float value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0) _gl.Uniform3(loc, value.X, value.Y, value.Z);
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc >= 0) _gl.UniformMatrix4(loc, 1, false, (float*)&value);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            throw new Exception($"{type} compile error: {info}");
        }

        return shader;
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
    }
}
