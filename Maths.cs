using Silk.NET.Maths;

public static class Maths
{
    public static Matrix4X4<float> CreatePerspectiveMatrix(float fov, float aspectRatio, float nearPlane, float farPlane)
    {
        return Matrix4X4.CreatePerspectiveFieldOfView(fov, aspectRatio, nearPlane, farPlane);
    }

    public static Matrix4X4<float> CreateLookAt(Vector3D<float> eye, Vector3D<float> center, Vector3D<float> up)
    {
        return Matrix4X4.CreateLookAt(eye, center, up);
    }
}
