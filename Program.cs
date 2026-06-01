using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using ImGuiNET;
using Silk.NET.Maths;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// --- ENGINE DATA STRUCTURES ---

public enum ShapeType { Cube, Pyramid, CustomModel }

public class RenderObject 
{
    public ShapeType Type { get; set; }
    public Vector3D<float> Position { get; set; }
    public Vector3D<float> Scale { get; set; }
    public float CustomRotation { get; set; }
    public Vector3D<float> ObjectColor { get; set; }
    public float[] CustomVertexData { get; set; } = Array.Empty<float>();
    public string ModelPath { get; set; } = "";
    public string TexturePath { get; set; } = "";
    public uint TextureId { get; set; }

    public RenderObject() { } // Empty constructor for JSON

    public RenderObject(ShapeType type, Vector3D<float> pos, Vector3D<float> color) 
    {
        Type = type; Position = pos; ObjectColor = color; Scale = Vector3D<float>.One;
    }
}

// Data models for saving/loading to JSON
public class SceneSaveData
{
    public bool ShowFloor { get; set; }
    public Vector3 MainLightPos { get; set; }
    public List<Vector3> PointLights { get; set; } = new();
    public List<ObjectSaveData> Objects { get; set; } = new();
}

public class ObjectSaveData
{
    public ShapeType Type { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Scale { get; set; }
    public float Rotation { get; set; }
    public Vector3 Color { get; set; }
    public string ModelPath { get; set; } = "";
    public string TexturePath { get; set; } = "";
}

// --- Supporting Engine Framework Classes ---

public static class ObjLoader
{
    public static float[] Load(string filePath)
    {
        if (!File.Exists(filePath)) return new float[0];

        List<Vector3D<float>> positions = new();
        List<Vector2D<float>> texCoords = new();
        List<Vector3D<float>> normals = new();
        List<float> interleavedData = new();

        using (StreamReader reader = new(filePath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("v "))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    positions.Add(new Vector3D<float>(float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                }
                else if (line.StartsWith("vt "))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    texCoords.Add(new Vector2D<float>(float.Parse(parts[1]), float.Parse(parts[2])));
                }
                else if (line.StartsWith("vn "))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    normals.Add(new Vector3D<float>(float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                }
                else if (line.StartsWith("f "))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i <= 3; i++) 
                    {
                        var vertexParts = parts[i].Split('/');
                        
                        int posIdx = int.Parse(vertexParts[0]) - 1;
                        var pos = positions[posIdx];
                        interleavedData.Add(pos.X); interleavedData.Add(pos.Y); interleavedData.Add(pos.Z);

                        if (vertexParts.Length > 1 && !string.IsNullOrEmpty(vertexParts[1]))
                        {
                            int texIdx = int.Parse(vertexParts[1]) - 1;
                            var tex = texCoords[texIdx];
                            interleavedData.Add(tex.X); interleavedData.Add(tex.Y);
                        }
                        else { interleavedData.Add(0.0f); interleavedData.Add(0.0f); }

                        if (vertexParts.Length > 2 && !string.IsNullOrEmpty(vertexParts[2]))
                        {
                            int normIdx = int.Parse(vertexParts[2]) - 1;
                            var norm = normals[normIdx];
                            interleavedData.Add(norm.X); interleavedData.Add(norm.Y); interleavedData.Add(norm.Z);
                        }
                        else { interleavedData.Add(0.0f); interleavedData.Add(1.0f); interleavedData.Add(0.0f); }
                    }
                }
            }
        }
        return interleavedData.ToArray();
    }
}

// Application State Machine
enum AppState { ProjectManager, EngineWorkspace }

class Program
{
    private static GL _gl = null!;
    private static IWindow _window = null!;
    private static IInputContext _input = null!;
    private static ImGuiController _imGuiController = null!;
    private static Shader? _shader;
    private static Cube? _cube;
    private static Pyramid? _pyramid;
    private static Floor? _floor;

    private static uint _depthMapFbo;
    private static uint _depthMapTexture;
    private const int ShadowWidth = 2048;
    private const int ShadowHeight = 2048;
    
    // LIGHTING SYSTEM VARIABLES
    private static Vector3D<float> _lightPos = new(5.0f, 8.0f, 5.0f);
    private static List<Vector3D<float>> _pointLights = new();

    private static Vector3D<float> _cameraPos = new(0.0f, 2.0f, 8.0f);
    private static Vector3D<float> _cameraFront = new(0.0f, -0.2f, -1.0f);
    private static Vector3D<float> _cameraUp = Vector3D<float>.UnitY;
    private static float _yaw = -90.0f;
    private static float _pitch = -10.0f;
    private const float CameraSpeed = 5.0f;
    private const float MouseSensitivity = 0.1f;

    private static float _time = 0.0f;
    public static int VertexCount;

    private static IMouse? _mouse;
    private static IKeyboard? _keyboard;
    private static Vector2D<float> _lastMousePos = Vector2D<float>.Zero;
    private static bool _firstMouseMove = true;
    private static bool _rightMousePressed = false;

    private static List<RenderObject> _worldObjects = new();
    private static int _selectedObjectIndex = 0;

    // PROJECT & BROWSER SYSTEM VARIABLES
    private static AppState _currentState = AppState.ProjectManager;
    private static string _projectBaseFolder = "Projects";
    private static string _currentProjectPath = "";
    private static string _newProjectName = "";
    private static string _contentBrowserCurrentDir = "";
    
    // TOGGLES
    private static bool _showFloor = false;

    static void Main()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "Silk.NET Test Graphics Engine";
        options.VSync = true;
        options.PreferredDepthBufferBits = 24; 

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();
        Cleanup();
    }

    private static void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();
        _imGuiController = new ImGuiController(_gl, _window, _input);

        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        if (_input.Mice.Count > 0) _mouse = _input.Mice[0];
        if (_input.Keyboards.Count > 0) _keyboard = _input.Keyboards[0];

        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);

        _shader = new Shader(_gl, "cube_vertex.glsl", "cube_fragment.glsl");
        _cube = new Cube(_gl, _shader);
        _pyramid = new Pyramid(_gl, _shader);
        _floor = new Floor(_gl, _shader);

        SetupDepthMap();
        SetupImGuiOpaqueTheme(); // Replaced the red theme with professional dark mode

        if (!Directory.Exists(_projectBaseFolder)) Directory.CreateDirectory(_projectBaseFolder);
    }

    private static void SetupImGuiOpaqueTheme()
    {
        ImGui.StyleColorsDark(); // Start with standard dark base
        var style = ImGui.GetStyle();
        
        style.WindowRounding = 4.0f;
        style.FrameRounding = 2.0f;
        
        // Remove ALL transparency (Alpha/W = 1.0f)
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.11f, 0.11f, 0.12f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.15f, 0.15f, 0.16f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.22f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.25f, 0.25f, 0.27f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.30f, 0.30f, 0.32f, 1.0f);
        
        // Solid Engine Blue accents
        var accentBlue = new Vector4(0.15f, 0.45f, 0.85f, 1.0f);
        var accentBlueHover = new Vector4(0.20f, 0.55f, 0.95f, 1.0f);
        var accentBlueActive = new Vector4(0.10f, 0.35f, 0.75f, 1.0f);

        style.Colors[(int)ImGuiCol.Header] = accentBlue;
        style.Colors[(int)ImGuiCol.HeaderHovered] = accentBlueHover;
        style.Colors[(int)ImGuiCol.HeaderActive] = accentBlueActive;
        style.Colors[(int)ImGuiCol.Button] = accentBlue;
        style.Colors[(int)ImGuiCol.ButtonHovered] = accentBlueHover;
        style.Colors[(int)ImGuiCol.ButtonActive] = accentBlueActive;
        style.Colors[(int)ImGuiCol.SliderGrab] = accentBlueHover;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accentBlueActive;
        style.Colors[(int)ImGuiCol.CheckMark] = accentBlueHover;
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.08f, 0.08f, 0.09f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.08f, 0.08f, 0.09f, 1.0f);
    }

    private static void OnUpdate(double delta)
    {
        if (_currentState == AppState.ProjectManager) return; 

        _time += (float)delta;
        if (_keyboard == null || _mouse == null) return;

        float moveDistance = CameraSpeed * (float)delta;
        var right = Vector3D.Normalize(Vector3D.Cross(_cameraFront, _cameraUp));

        if (_keyboard.IsKeyPressed(Key.W)) _cameraPos += _cameraFront * moveDistance;
        if (_keyboard.IsKeyPressed(Key.S)) _cameraPos -= _cameraFront * moveDistance;
        if (_keyboard.IsKeyPressed(Key.A)) _cameraPos -= right * moveDistance;
        if (_keyboard.IsKeyPressed(Key.D)) _cameraPos += right * moveDistance;
        if (_keyboard.IsKeyPressed(Key.Space)) _cameraPos += _cameraUp * moveDistance;
        if (_keyboard.IsKeyPressed(Key.ShiftLeft)) _cameraPos -= _cameraUp * moveDistance;

        bool rmb = _mouse.IsButtonPressed(MouseButton.Right);
        if (rmb && !_rightMousePressed)
        {
            _rightMousePressed = true;
            if (_mouse.Cursor != null) _mouse.Cursor.CursorMode = CursorMode.Disabled;
            _firstMouseMove = true;
        }
        else if (!rmb && _rightMousePressed)
        {
            _rightMousePressed = false;
            if (_mouse.Cursor != null) _mouse.Cursor.CursorMode = CursorMode.Normal;
        }

        if (_rightMousePressed) UpdateCameraFromMouse();
    }

    private static void UpdateCameraFromMouse()
    {
        if (_mouse == null) return;
        var currentMousePos = new Vector2D<float>(_mouse.Position.X, _mouse.Position.Y);

        if (_firstMouseMove)
        {
            _lastMousePos = currentMousePos;
            _firstMouseMove = false;
            return;
        }

        var deltaX = currentMousePos.X - _lastMousePos.X;
        var deltaY = currentMousePos.Y - _lastMousePos.Y;
        _lastMousePos = currentMousePos;

        _yaw += deltaX * MouseSensitivity;
        _pitch -= deltaY * MouseSensitivity;
        _pitch = Math.Clamp(_pitch, -89.0f, 89.0f);

        float yawRad = MathF.PI * _yaw / 180.0f;
        float pitchRad = MathF.PI * _pitch / 180.0f;

        _cameraFront = Vector3D.Normalize(new Vector3D<float>(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        ));
    }

    private static void OnRender(double delta)
    {
        _imGuiController.Update((float)delta);
        var fbSize = _window.FramebufferSize;
        if (fbSize.Y == 0) fbSize.Y = 1;

        if (_currentState == AppState.ProjectManager)
        {
            _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            _gl.ClearColor(0.08f, 0.08f, 0.09f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            
            RenderProjectManagerUI(fbSize);
            _imGuiController.Render();
            return;
        }

        // --- ENGINE 3D WORKSPACE RENDER PASS ---

        var lightProjection = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 2.5f, 1.0f, 0.1f, 25.0f);
        var lightView = Matrix4X4.CreateLookAt(_lightPos, Vector3D<float>.Zero, Vector3D<float>.UnitY);
        var lightSpaceMatrix = lightProjection * lightView;

        // PASS 1: SHADOWS
        _gl.Viewport(0, 0, ShadowWidth, ShadowHeight);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _depthMapFbo);
        _gl.Clear(ClearBufferMask.DepthBufferBit);

        _shader!.Use();
        _shader.SetBool("isDepthPass", true);
        _shader.SetMatrix4("lightSpaceMatrix", lightSpaceMatrix);

        var floorModel = Matrix4X4<float>.Identity;
        
        if (_showFloor)
        {
            _shader.SetMatrix4("model", floorModel);
            _floor!.Draw(false);
        }

        foreach (var obj in _worldObjects)
        {
            var objModel = Matrix4X4.CreateScale(obj.Scale) *
                          Matrix4X4.CreateFromAxisAngle(Vector3D<float>.UnitY, obj.CustomRotation) *
                          Matrix4X4.CreateTranslation(obj.Position);
            _shader.SetMatrix4("model", objModel);
            RenderObjectGeometry(obj, false);
        }

        // PASS 2: VIEWPORT GRAPHICS
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
        _gl.ClearColor(0.11f, 0.11f, 0.12f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _depthMapTexture);

        _shader.SetBool("isDepthPass", false);
        _shader.SetInt("shadowMap", 0);
        _shader.SetMatrix4("lightSpaceMatrix", lightSpaceMatrix);
        _shader.SetVector3("lightPos", _lightPos);
        _shader.SetVector3("viewPos", _cameraPos);
        _shader.SetVector3("ambientStrength", new Vector3D<float>(0.15f, 0.15f, 0.15f));
        _shader.SetVector3("diffuseStrength", new Vector3D<float>(0.7f, 0.7f, 0.7f));
        _shader.SetVector3("specularStrength", new Vector3D<float>(0.5f, 0.5f, 0.5f));

        _shader.SetInt("pointLightCount", _pointLights.Count);
        for(int i = 0; i < _pointLights.Count; i++)
        {
            _shader.SetVector3($"pointLights[{i}].position", _pointLights[i]);
        }

        var view = Matrix4X4.CreateLookAt(_cameraPos, _cameraPos + _cameraFront, _cameraUp);
        float aspectRatio = (float)fbSize.X / (float)fbSize.Y;
        var projection = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspectRatio, 0.1f, 100f);

        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        if (_showFloor)
        {
            _shader.SetMatrix4("model", floorModel);
            _shader.SetVector3("objectColor", new Vector3D<float>(0.4f, 0.4f, 0.4f));
            _shader.SetBool("useTexture", false);
            _floor.Draw();
        }

        foreach (var obj in _worldObjects)
        {
            var objModel = Matrix4X4.CreateScale(obj.Scale) *
                          Matrix4X4.CreateFromAxisAngle(Vector3D<float>.UnitY, obj.CustomRotation) *
                          Matrix4X4.CreateTranslation(obj.Position);
            _shader.SetMatrix4("model", objModel);
            _shader.SetVector3("objectColor", obj.ObjectColor);

            if (obj.TextureId != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, obj.TextureId);
                _shader.SetInt("objectTexture", 1);
                _shader.SetBool("useTexture", true);
            }
            else
            {
                _shader.SetBool("useTexture", false);
            }

            RenderObjectGeometry(obj, true);
        }

        RenderImGuiWorkspace();
        _imGuiController.Render();
    }

    private static void RenderProjectManagerUI(Vector2D<int> screenSize)
    {
        ImGui.SetNextWindowPos(new Vector2(screenSize.X / 2f - 300, screenSize.Y / 2f - 200));
        ImGui.SetNextWindowSize(new Vector2(600, 400));
        ImGui.Begin("Project Manager", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "CREATE OR LOAD PROJECT");
        ImGui.Separator();

        ImGui.InputText("New Project Name", ref _newProjectName, 128);
        if (ImGui.Button("Create New Project", new Vector2(-1, 35)))
        {
            if (!string.IsNullOrWhiteSpace(_newProjectName))
            {
                string targetDir = Path.Combine(_projectBaseFolder, _newProjectName);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                LoadProject(targetDir);
            }
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "EXISTING PROJECTS");
        
        var dirs = Directory.GetDirectories(_projectBaseFolder);
        foreach (var dir in dirs)
        {
            string projName = Path.GetFileName(dir);
            if (ImGui.Button($"Load {projName}", new Vector2(-1, 30)))
            {
                LoadProject(dir);
            }
        }

        ImGui.End();
    }

private static void SaveScene()
{
    var data = new SceneSaveData
    {
        ShowFloor = _showFloor,
        MainLightPos = new Vector3(_lightPos.X, _lightPos.Y, _lightPos.Z),
        PointLights = new List<Vector3>(),
        Objects = new List<ObjectSaveData>()
    };

    foreach (var l in _pointLights)
        data.PointLights.Add(new Vector3(l.X, l.Y, l.Z));

    foreach (var o in _worldObjects)
    {
        data.Objects.Add(new ObjectSaveData
        {
            Type = o.Type,
            Position = new Vector3(o.Position.X, o.Position.Y, o.Position.Z),
            Scale = new Vector3(o.Scale.X, o.Scale.Y, o.Scale.Z),
            Rotation = o.CustomRotation,
            Color = new Vector3(o.ObjectColor.X, o.ObjectColor.Y, o.ObjectColor.Z),
            ModelPath = o.ModelPath,
            TexturePath = o.TexturePath
        });
    }

    var path = Path.Combine(_currentProjectPath, "scene.json");

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(path, json);
}
    private static void LoadProject(string directoryPath)
    {
        _currentProjectPath = directoryPath;
        _contentBrowserCurrentDir = _currentProjectPath; 
        _worldObjects.Clear();
        _pointLights.Clear();

        string sceneFile = Path.Combine(_currentProjectPath, "scene.json");
        if (File.Exists(sceneFile))
        {
            string json = File.ReadAllText(sceneFile);
            var data = JsonSerializer.Deserialize<SceneSaveData>(json);
            if (data != null)
            {
                _showFloor = data.ShowFloor;
                _lightPos = new Vector3D<float>(data.MainLightPos.X, data.MainLightPos.Y, data.MainLightPos.Z);
                
                foreach (var l in data.PointLights)
                {
                    _pointLights.Add(new Vector3D<float>(l.X, l.Y, l.Z));
                }

                foreach (var o in data.Objects)
{
    var newObj = new RenderObject(
        o.Type,
        new Vector3D<float>(o.Position.X, o.Position.Y, o.Position.Z),
        new Vector3D<float>(o.Color.X, o.Color.Y, o.Color.Z)
    );

    newObj.Scale = new Vector3D<float>(o.Scale.X, o.Scale.Y, o.Scale.Z);
    newObj.CustomRotation = o.Rotation;

    newObj.ModelPath = o.ModelPath;
    newObj.TexturePath = o.TexturePath;

    newObj.TextureId = 0;
    newObj.CustomVertexData = Array.Empty<float>();

    if (o.Type == ShapeType.CustomModel && !string.IsNullOrEmpty(o.ModelPath))
        newObj.CustomVertexData = ObjLoader.Load(o.ModelPath);

    if (!string.IsNullOrEmpty(o.TexturePath))
        newObj.TextureId = LoadGlTexture(o.TexturePath);

    _worldObjects.Add(newObj);
}
            }
        }
        
        _currentState = AppState.EngineWorkspace;
    }

    private static void RenderObjectGeometry(RenderObject obj, bool countVertices)
    {
        switch (obj.Type)
        {
            case ShapeType.Cube: _cube?.Draw(countVertices); break;
            case ShapeType.Pyramid: _pyramid?.Draw(countVertices); break;
            case ShapeType.CustomModel:
                if (obj.CustomVertexData != null && obj.CustomVertexData.Length > 0)
                {
                    if (countVertices) VertexCount += obj.CustomVertexData.Length / 8;
                }
                break;
        }
    }

    private static void RenderImGuiWorkspace()
    {
        uint dockspaceId = ImGui.GetID("MainDockSpace");
        ImGui.DockSpaceOverViewport(dockspaceId, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        // --- Panel 1: Main Config Control Panel ---
        ImGui.Begin("Engine Control Panel");
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "GRAPHICS ENGINE METRICS");
        ImGui.Separator();
        ImGui.Text($"FPS: {ImGui.GetIO().Framerate:F1}");
        ImGui.Text($"Total World Vertices: {VertexCount}");
        ImGui.Separator();
        ImGui.Checkbox("Render Virtual Ground Floor", ref _showFloor);
        ImGui.Separator();
        
        if (ImGui.Button("SAVE CURRENT SCENE", new Vector2(-1, 35)))
        {
            SaveScene();
        }
        if (ImGui.Button("RETURN TO PROJECT MANAGER", new Vector2(-1, 35)))
        {
            SaveScene(); // Auto-save on exit
            _currentState = AppState.ProjectManager;
        }
        ImGui.End();

        // --- Panel 2: Asset Transform Inspector ---
        ImGui.Begin("Object Inspector");
        
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "GEOMETRY ENTITIES");
        if (_worldObjects.Count > 0)
        {
            if (_selectedObjectIndex >= _worldObjects.Count) _selectedObjectIndex = 0;
            
            for (int i = 0; i < _worldObjects.Count; i++)
            {
                bool activeSel = (_selectedObjectIndex == i);
                if (ImGui.Selectable($"Instance [{i}]: {_worldObjects[i].Type}", activeSel)) _selectedObjectIndex = i;
            }

            ImGui.Separator();
            var targetMesh = _worldObjects[_selectedObjectIndex];

            var nPos = new Vector3(targetMesh.Position.X, targetMesh.Position.Y, targetMesh.Position.Z);
            if (ImGui.DragFloat3("Position Space Matrix", ref nPos, 0.05f)) targetMesh.Position = new Vector3D<float>(nPos.X, nPos.Y, nPos.Z);

            var nScale = new Vector3(targetMesh.Scale.X, targetMesh.Scale.Y, targetMesh.Scale.Z);
            if (ImGui.DragFloat3("Scale Matrix Bounds", ref nScale, 0.02f, 0.1f, 10f)) targetMesh.Scale = new Vector3D<float>(nScale.X, nScale.Y, nScale.Z);

            float rotVal = targetMesh.CustomRotation;
            if (ImGui.SliderAngle("Rotation Yaw", ref rotVal)) targetMesh.CustomRotation = rotVal;

            ImGui.Separator();
            if (ImGui.Button("Delete Selected Mesh", new Vector2(-1, 25)))
            {
                _worldObjects.RemoveAt(_selectedObjectIndex);
                if (_selectedObjectIndex > 0) _selectedObjectIndex--;
            }
        }
        else
        {
            ImGui.Text("No mesh entities to inspect.");
        }
        
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "LIGHTING MANAGEMENT");
        var lPos = new Vector3(_lightPos.X, _lightPos.Y, _lightPos.Z);
        if (ImGui.DragFloat3("Main Sun/Shadow Light", ref lPos, 0.1f)) _lightPos = new Vector3D<float>(lPos.X, lPos.Y, lPos.Z);

        for (int i = 0; i < _pointLights.Count; i++)
        {
            ImGui.PushID($"light_{i}");
            var pPos = new Vector3(_pointLights[i].X, _pointLights[i].Y, _pointLights[i].Z);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 40); // Leave room for X button
            if (ImGui.DragFloat3($"Point Light [{i}]", ref pPos, 0.1f)) _pointLights[i] = new Vector3D<float>(pPos.X, pPos.Y, pPos.Z);
            
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); // Red delete button
            if (ImGui.Button("X", new Vector2(30, 0))) 
            { 
                _pointLights.RemoveAt(i); 
                i--; 
            }
            ImGui.PopStyleColor();
            ImGui.PopID();
        }

        ImGui.End();

        // --- Panel 3: Spawner ---
        ImGui.Begin("Spawner");
        if (ImGui.Button("Spawn Primitive Cube", new Vector2(-1, 25)))
            _worldObjects.Add(new RenderObject(ShapeType.Cube, _cameraPos + _cameraFront * 3.0f, new Vector3D<float>(0.2f, 0.6f, 0.8f)));
        if (ImGui.Button("Spawn Primitive Pyramid", new Vector2(-1, 25)))
            _worldObjects.Add(new RenderObject(ShapeType.Pyramid, _cameraPos + _cameraFront * 3.0f, new Vector3D<float>(0.8f, 0.2f, 0.2f)));
        if (ImGui.Button("Spawn Dynamic Point Light", new Vector2(-1, 25)))
            _pointLights.Add(_cameraPos + _cameraFront * 2.0f);
        ImGui.End();

        // --- Panel 4: Content Browser (File System Drop-In) ---
        ImGui.Begin("Content Browser");

ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), $"Path: {_contentBrowserCurrentDir}");
ImGui.Separator();

if (_contentBrowserCurrentDir != _currentProjectPath)
{
    if (ImGui.Button("[ .. ] Up Directory", new Vector2(-1, 25)))
    {
        var parent = Directory.GetParent(_contentBrowserCurrentDir);
        if (parent != null) _contentBrowserCurrentDir = parent.FullName;
    }
}

string[] files;
string[] dirs;

try
{
    files = Directory.GetFiles(_contentBrowserCurrentDir);
    dirs = Directory.GetDirectories(_contentBrowserCurrentDir);
}
catch (Exception ex)
{
    ImGui.TextColored(new Vector4(1, 0, 0, 1), ex.Message);
    ImGui.End();
    return;
}

// DIRECTORIES FIRST
foreach (var dir in dirs)
{
    if (ImGui.Button($"[DIR] {Path.GetFileName(dir)}", new Vector2(-1, 22)))
        _contentBrowserCurrentDir = dir;
}

ImGui.Separator();

// FILES
foreach (var file in files)
{
    string ext = Path.GetExtension(file).ToLower();
    string fileName = Path.GetFileName(file);

    if (ext == ".obj")
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));

        if (ImGui.Button($"[MODEL] {fileName}", new Vector2(-1, 25)))
        {
            float[] data = ObjLoader.Load(file);
            if (data.Length > 0)
            {
                var customObj = new RenderObject(
                    ShapeType.CustomModel,
                    _cameraPos + _cameraFront * 4.0f,
                    Vector3D<float>.One
                )
                {
                    CustomVertexData = data,
                    ModelPath = file
                };

                _worldObjects.Add(customObj);
                _selectedObjectIndex = _worldObjects.Count - 1;
            }
        }

        ImGui.PopStyleColor();
    }
    else if (ext == ".png" || ext == ".jpg")
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.8f, 1.0f));

        if (ImGui.Button($"[TEXTURE] {fileName}", new Vector2(-1, 25)))
        {
            if (_worldObjects.Count > 0)
            {
                var targetMesh = _worldObjects[_selectedObjectIndex];
                targetMesh.TexturePath = file;
                targetMesh.TextureId = LoadGlTexture(file);
            }
        }

        ImGui.PopStyleColor();
    }
    else
    {
        ImGui.Text($"[FILE] {fileName}");
    }
}

ImGui.End();
    }
    private static uint LoadGlTexture(string path)
    {
        if (!File.Exists(path)) return 0;
        uint texHandle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texHandle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return texHandle;
    }

    private static void SetupDepthMap()
    {
        _depthMapFbo = _gl.GenFramebuffer();
        _depthMapTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _depthMapTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, (uint)ShadowWidth, (uint)ShadowHeight, 0, PixelFormat.DepthComponent, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            var borderColor = new float[] { 1f, 1f, 1f, 1f };
            fixed (float* pointer = borderColor) _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, pointer);
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _depthMapFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _depthMapTexture, 0);
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private static void OnClosing() => Cleanup();

    private static void Cleanup()
    {
        _cube?.Dispose();
        _pyramid?.Dispose();
        _floor?.Dispose();
        _shader?.Dispose();
        _imGuiController?.Dispose();
        _window?.Dispose();
    }
    public class Mesh : IDisposable
{
    private readonly GL _gl;

    private uint _vao;
    private uint _vbo;
    private int _vertexCount;

    public Mesh(GL gl, float[] vertexData)
    {
        _gl = gl;

        _vertexCount = vertexData.Length / 8; // pos(3) + uv(2) + normal(3)

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer,
            (nuint)(vertexData.Length * sizeof(float)),
            vertexData,
            BufferUsageARB.StaticDraw);

        unsafe
        {
            int stride = 8 * sizeof(float);

            // POSITION (location 0)
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
            _gl.EnableVertexAttribArray(0);

            // TEXCOORD (location 1)
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // NORMAL (location 2)
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(5 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }

        _gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
}