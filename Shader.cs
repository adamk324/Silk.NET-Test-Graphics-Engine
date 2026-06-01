using Silk.NET.OpenGL;
using Silk.NET.Maths;
using System.IO;

public class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _programId;

    public Shader(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;
        var vertexCode = File.ReadAllText(vertexPath);
        var fragmentCode = File.ReadAllText(fragmentPath);

        _programId = _gl.CreateProgram();

        CompileShader(_programId, ShaderType.VertexShader, vertexCode);
        CompileShader(_programId, ShaderType.FragmentShader, fragmentCode);

        _gl.LinkProgram(_programId);

        if (_gl.GetProgram(_programId, ProgramPropertyARB.LinkStatus) == 0)
        {
            var infoLog = _gl.GetProgramInfoLog(_programId);
            throw new InvalidOperationException($"Failed to link program: {infoLog}");
        }
    }

    private void CompileShader(uint program, ShaderType type, string source)
    {
        var shaderId = _gl.CreateShader(type);
        _gl.ShaderSource(shaderId, source);
        _gl.CompileShader(shaderId);

        if (_gl.GetShader(shaderId, ShaderParameterName.CompileStatus) == 0)
        {
            var error = _gl.GetShaderInfoLog(shaderId);
            _gl.DeleteShader(shaderId);
            throw new InvalidOperationException($"Failed to compile {type}: {error}");
        }

        _gl.AttachShader(program, shaderId);
        _gl.DeleteShader(shaderId);
    }

    public void Use()
    {
        _gl.UseProgram(_programId);
    }

    public void SetVector3(string name, Vector3D<float> value)
    {
        var location = _gl.GetUniformLocation(_programId, name);
        if (location < 0)
        {
            return;
        }

        _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetBool(string name, bool value)
    {
        var location = _gl.GetUniformLocation(_programId, name);
        if (location < 0)
        {
            return;
        }

        _gl.Uniform1(location, value ? 1 : 0);
    }

    public void SetFloat(string name, float value)
    {
        var location = _gl.GetUniformLocation(_programId, name);
        if (location < 0)
        {
            return;
        }

        _gl.Uniform1(location, value);
    }

    public void SetInt(string name, int value)
    {
        var location = _gl.GetUniformLocation(_programId, name);
        if (location < 0)
        {
            return;
        }

        _gl.Uniform1(location, value);
    }

    public unsafe void SetMatrix4(string name, Matrix4X4<float> value)
    {
        var location = _gl.GetUniformLocation(_programId, name);
        if (location < 0)
        {
            return;
        }

        var matrix = new float[]
        {
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44
        };

        fixed (float* pointer = matrix)
        {
            _gl.UniformMatrix4(location, 1, false, pointer);
        }
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_programId);
        GC.SuppressFinalize(this);
    }
}
