using Silk.NET.OpenGL;
using Silk.NET.Maths;

public class Floor : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Shader _shader;
    private const int VertexSize = 6 * sizeof(float);
    public const int VertexCount = 6;

    public unsafe Floor(GL gl, Shader shader)
    {
        _gl = gl;
        _shader = shader;

        var vertices = new float[]
        {
            -5.0f, 0.0f, -5.0f,  0.0f, 1.0f, 0.0f,
             5.0f, 0.0f, -5.0f,  0.0f, 1.0f, 0.0f,
             5.0f, 0.0f,  5.0f,  0.0f, 1.0f, 0.0f,
             5.0f, 0.0f,  5.0f,  0.0f, 1.0f, 0.0f,
            -5.0f, 0.0f,  5.0f,  0.0f, 1.0f, 0.0f,
            -5.0f, 0.0f, -5.0f,  0.0f, 1.0f, 0.0f
        };

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        unsafe
        {
            fixed (float* vertexData = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), vertexData, BufferUsageARB.StaticDraw);
            }
        }

        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)VertexSize, (void*)0);
        _gl.EnableVertexAttribArray(0);

        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)VertexSize, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void Draw(bool countVertices = true)
    {
        _shader.Use();
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        if (countVertices)
        {
            Program.VertexCount += VertexCount;
        }
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
