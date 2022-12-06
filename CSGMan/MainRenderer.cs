using Veldrid.Sdl2;
using Veldrid;
using Veldrid.StartupUtilities;
using System.Diagnostics;
using ImGuiNET;
using System.Numerics;
using System.Text;
using Veldrid.SPIRV;
using System.Runtime.InteropServices;
using CSG;
using CSG.Shapes;

namespace CSGMan
{
    internal class MainRenderer : IDisposable
    {
        private Sdl2Window _window;
        private GraphicsDevice _gd;

        private ResourceFactory _factory;

        private CommandList _cl;

        private const float _mouseSensitivity = 0.5f;

        private static Texture? _viewportTex;
        private static nint _viewportTexId = -1;
        private static Texture? _viewportDepthTex;
        private static Framebuffer? _viewportFb;
        private static Vector2 _lastViewportSize = Vector2.Zero;
        private static bool _isMouseControllingViewport = false;
        private static Vector2 _lastViewportMousePos = Vector2.Zero;
        private static float _viewportYaw = -90.0f;
        private static float _viewportPitch = 0.0f;

        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;
        private static uint _indexCount;

        private static DeviceBuffer _cameraBuffer;

        private static ResourceSet _resourceSet;
        private static ResourceLayout _resourceLayout;

        private static Shader[] _shaders;
        private static Pipeline? _pipeline = null;
        private static GraphicsPipelineDescription _pipelineDescription;

        private static Vector3 _camPos;
        private static Vector3 _camFwd;
        private static Vector3 _camUp = Vector3.UnitY;

        private struct CameraInfo
        {
            public Matrix4x4 vp;
            public Vector4 pos;
        }

        private ImGuiRenderer _imguiRenderer;

        private ulong _lastTimeNS = 0;
        private Stopwatch _deltaTimer = new();

        public bool isDisposed { get; private set; }

        private const string _vertexCode = @"
#version 460

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 UV;
layout(location = 3) in vec4 Color;

layout(location = 0) out vec3 fsin_Normal;
layout(location = 1) out flat vec3 fsin_LightDir;

layout(set = 0, binding = 0) uniform CameraBuffer
{
    mat4 vp;
    vec3 camPos;
};

void main()
{
    gl_Position = vp * vec4(Position, 1);

    fsin_LightDir = normalize(camPos);
    fsin_Normal = Normal;
}";

        private const string _fragmentCode = @"
#version 460

layout(location = 0) in vec3 fsin_Normal;
layout(location = 1) in flat vec3 fsin_LightDir;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    float light = dot(fsin_LightDir, fsin_Normal);
    fsout_Color = vec4(vec3(light), 1);
}";

        public MainRenderer()
        {
            _window = VeldridStartup.CreateWindow(new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "CSGMan"
            });

            _gd = VeldridStartup.CreateGraphicsDevice(_window, new GraphicsDeviceOptions()
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                ResourceBindingModel = ResourceBindingModel.Improved,
                Debug = true
            });
            _factory = _gd.ResourceFactory;

            _cameraBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CameraInfo>(), 
                BufferUsage.UniformBuffer));

            Cylinder shape1 = new(start: new Vector3(0, 2, 0), end: new Vector3(0, -2, 0), radius: 2, tessellation: 16);
            Cube shape2 = new(position: new Vector3(0, 0, 0), size: new Vector3(2, 1, 1));
            var shape3 = shape1.Subtract(shape2);
            Cube shape4 = new(position: new Vector3(0, 0, 0), size: new Vector3(3, 2, 3));
            var result = shape4.Subtract(shape3);

            _vertexBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)result.Vertices.Length * Vertex.SizeInBytes,
                BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)result.Indices.Length * sizeof(uint),
                BufferUsage.IndexBuffer));

            _gd.UpdateBuffer(_vertexBuffer, 0, result.Vertices);
            _gd.UpdateBuffer(_indexBuffer, 0, result.Indices);

            _indexCount = (uint)result.Indices.Length;

            _resourceLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _cameraBuffer));

            VertexLayoutDescription vertexLayout = new(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            ShaderDescription vertexShaderDesc = new(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(_vertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(_fragmentCode),
                "main");

            _shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            _pipelineDescription = new();
            _pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
            _pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);
            _pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);
            _pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleList;
            _pipelineDescription.ResourceLayouts = new ResourceLayout[] { _resourceLayout };
            _pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);

            _cl = _gd.ResourceFactory.CreateCommandList();
            _imguiRenderer = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription,
                (int)_gd.MainSwapchain.Framebuffer.Width, (int)_gd.MainSwapchain.Framebuffer.Height);

            _camFwd = new Vector3(0.0f, 0.0f, -1.0f);
            _camPos = new Vector3(0.0f, 0.0f, 4.0f);

            _deltaTimer.Start();

            _window.Resized += () =>
            {
                _gd.ResizeMainWindow((uint)_window.Width, (uint)_window.Height);

                _imguiRenderer.WindowResized(
                    (int)_gd.MainSwapchain.Framebuffer.Width, 
                    (int)_gd.MainSwapchain.Framebuffer.Height);
            };
        }

        public bool ShouldStop()
        {
            return !_window.Exists;
        }

        private void ResizeViewport(Vector2 size)
        {
            _gd.WaitForIdle();

            if (_viewportTex != null)
            {
                _imguiRenderer.RemoveImGuiBinding(_viewportTex);
                _viewportTex.Dispose();
            }
            if (_viewportDepthTex != null)
                _viewportDepthTex.Dispose();
            if (_viewportFb != null)
                _viewportFb.Dispose();

            _viewportTex = _factory.CreateTexture(new TextureDescription(
                (uint)size.X, (uint)size.Y, 1, 1, 1, 
                PixelFormat.R8_G8_B8_A8_UNorm, 
                TextureUsage.Sampled | TextureUsage.RenderTarget, 
                TextureType.Texture2D));
            _viewportDepthTex = _factory.CreateTexture(new TextureDescription(
                (uint)size.X, (uint)size.Y, 1, 1, 1,
                PixelFormat.D24_UNorm_S8_UInt,
                TextureUsage.DepthStencil,
                TextureType.Texture2D));
            _viewportFb = _factory.CreateFramebuffer(new FramebufferDescription(
                _viewportDepthTex, _viewportTex));
            _viewportTexId = _imguiRenderer.GetOrCreateImGuiBinding(_factory, _viewportTex);

            if (_pipeline == null)
            {
                _pipelineDescription.Outputs = _viewportFb.OutputDescription;
                _pipeline = _factory.CreateGraphicsPipeline(_pipelineDescription);
            }
        }

        public void Render()
        {
            InputSnapshot input = _window.PumpEvents();

            ulong timeNS = (ulong)(((float)_deltaTimer.ElapsedTicks / Stopwatch.Frequency) * 1000000000.0);
            ulong deltaTimeNS = timeNS - _lastTimeNS;
            double deltaTime = deltaTimeNS / 1000000000.0;

            _imguiRenderer.Update((float)deltaTime, input);

            float windowWidth = _window.Width;
            float windowHeight = _window.Height;

            float sidebarWidth = windowWidth * 0.15f;

            ImGui.SetNextWindowSize(new Vector2(sidebarWidth, windowHeight), ImGuiCond.Always);
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.Begin("Sidebar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
            }
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - sidebarWidth) / 2.0f, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth, 0.0f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("TopLeftViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                Vector2 viewportMousePos = ImGui.GetMousePos() - ImGui.GetWindowPos();
                Vector2 deltaMouse = viewportMousePos - _lastViewportMousePos;
                if (ImGui.IsWindowHovered())
                    _isMouseControllingViewport = ImGui.IsMouseDown(ImGuiMouseButton.Right);
                else
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                        _isMouseControllingViewport = false;
                }

                if (_isMouseControllingViewport)
                {
                    _viewportYaw += deltaMouse.X * _mouseSensitivity;
                    _viewportPitch += deltaMouse.Y * -_mouseSensitivity;

                    if (_viewportPitch > 89.0f)
                        _viewportPitch = 89.0f;
                    if (_viewportPitch < -89.0f)
                        _viewportPitch = -89.0f;
                }

                _camFwd.X = 
                    MathF.Cos(MathF.PI * _viewportYaw / 180) *
                    MathF.Cos(MathF.PI * _viewportPitch / 180);
                _camFwd.Y = 
                    MathF.Sin(MathF.PI * _viewportPitch / 180);
                _camFwd.Z =
                    MathF.Sin(MathF.PI * _viewportYaw / 180) *
                    MathF.Cos(MathF.PI * _viewportPitch / 180);
                _camFwd = Vector3.Normalize(_camFwd);

                var camRight = Vector3.Cross(_camFwd, Vector3.UnitY);
                _camUp = Vector3.Cross(camRight, _camFwd);

                if (ImGui.IsWindowHovered() || _isMouseControllingViewport)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.D))
                        _camPos += camRight * (float)deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.A))
                        _camPos += -camRight * (float)deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.W))
                        _camPos += _camFwd * (float)deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.S))
                        _camPos += -_camFwd * (float)deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.Space))
                        _camPos += Vector3.UnitY * (float)deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                        _camPos += -Vector3.UnitY * (float)deltaTime * 10.0f;
                }

                Vector2 contentRegion = ImGui.GetContentRegionAvail();
                if (contentRegion.X > 1 && contentRegion.Y > 1)
                {
                    if (contentRegion != _lastViewportSize)
                    {
                        ResizeViewport(contentRegion);
                    }
                    _lastViewportSize = contentRegion;

                    ImGui.Image(_viewportTexId, contentRegion);
                }

                _lastViewportMousePos = viewportMousePos;
            }
            ImGui.PopStyleVar();
            ImGui.End();

            float width = _viewportFb.Width;
            float height = _viewportFb.Height;
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(90.0f * (MathF.PI / 180.0f), width / height, 0.01f, 1000.0f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(_camPos, _camPos + _camFwd, _camUp);

            CameraInfo info = new()
            {
                vp = view * projection,
                pos = new Vector4(_camPos, 1.0f)
            };

            _cl.Begin();
            {
                _cl.SetFramebuffer(_viewportFb);
                _cl.ClearColorTarget(0, new RgbaFloat(0.25f, 0.25f, 0.25f, 1.0f));
                _cl.ClearDepthStencil(1);

                _cl.UpdateBuffer(_cameraBuffer, 0, info);

                _cl.SetPipeline(_pipeline);

                _cl.SetVertexBuffer(0, _vertexBuffer);
                _cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
                _cl.SetGraphicsResourceSet(0, _resourceSet);
                _cl.DrawIndexed(
                    indexCount: _indexCount,
                    instanceCount: 1,
                    indexStart: 0,
                    vertexOffset: 0,
                    instanceStart: 0);

                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, RgbaFloat.Black);
                _imguiRenderer.Render(_gd, _cl);                
            }
            _cl.End();

            _gd.SubmitCommands(_cl);

            // Technically this is inefficient.
            // However this is just a tool, so I'm not going to concern my self with optimal API usage.
            // I'd rather actually get it done.
            _gd.WaitForIdle(); 

            _gd.SwapBuffers();

            _lastTimeNS = timeNS;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    _gd.WaitForIdle();

                    _viewportTex?.Dispose();
                    _viewportDepthTex?.Dispose();
                    _viewportFb?.Dispose();
                    
                    _imguiRenderer.Dispose();
                    _cameraBuffer.Dispose();
                    _pipeline?.Dispose();
                    foreach (Shader shader in _shaders)
                        shader.Dispose();
                    _resourceLayout.Dispose();
                    _resourceSet.Dispose();
                    _cl.Dispose();
                    _vertexBuffer.Dispose();
                    _indexBuffer.Dispose();
                    _gd.Dispose();
                }

                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
