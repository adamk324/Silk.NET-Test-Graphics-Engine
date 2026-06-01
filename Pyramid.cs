using Silk.NET.OpenGL;
using Silk.NET.Maths;

public class Pyramid : IDisposable
{
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly GL _gl;
    private readonly Shader _shader;
    private const int VertexSize = 6 * sizeof(float);
    public const int VertexCount = 18;

    public Pyramid(GL gl, Shader shader)
    {
        _gl = gl;
        _shader = shader;

        // Pyramid: base centered at origin, peak at y=0.5f
        var vertices = new float[]
        {
            // Front face (triangle)
            -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
             0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
             0.0f,  0.5f,  0.0f,  0.0f,  0.0f,  1.0f,

            // Right face (triangle)
             0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f,
             0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,
             0.0f,  0.5f,  0.0f,  1.0f,  0.0f,  0.0f,

            // Back face (triangle)
             0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
            -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
             0.0f,  0.5f,  0.0f,  0.0f,  0.0f, -1.0f,

            // Left face (triangle)
            -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,
            -0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f,
             0.0f,  0.5f,  0.0f, -1.0f,  0.0f,  0.0f,

            // Bottom face quad as two triangles
            -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
             0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
             0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,

            -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
             0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,
            -0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,
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

        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)VertexSize, (void*)0);
            _gl.EnableVertexAttribArray(0);

            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)VertexSize, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void Draw(bool countVertices = true)
    {
        _shader.Use();
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, VertexCount);
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
