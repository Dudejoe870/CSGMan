using Veldrid.Sdl2;
using Veldrid;
using Veldrid.StartupUtilities;
using System.Diagnostics;
using ImGuiNET;
using System.Numerics;
using System.Text;
using Veldrid.SPIRV;
using CSG;
using CSG.Shapes;
using System.Runtime.CompilerServices;

namespace CSGMan
{
    public class MainRenderer : IDisposable
    {
        private CSGScene scene;

        private Sdl2Window _window;
        private GraphicsDevice _gd;

        private ResourceFactory _factory;

        private CommandList _cl;

        private const float _mouseSensitivity = 0.5f;

        public const PixelFormat colorFormat = PixelFormat.R8_G8_B8_A8_UNorm;
        public const PixelFormat depthFormat = PixelFormat.D24_UNorm_S8_UInt;

        private struct GpuCameraInfo
        {
            public Matrix4x4 vp;
            public Vector4 pos;
        }

        private struct Camera : IDisposable
        {
            private GraphicsDevice _gd;

            public DeviceBuffer cameraInfoBuffer;
            public GpuCameraInfo cameraInfo = new();
            public ResourceSet resourceSet;

            public Vector3 position = new(0.0f, 0.0f, 4.0f);
            public Vector3 forward = -Vector3.UnitZ;
            public Vector3 up = Vector3.UnitY;

            public Camera(GraphicsDevice gd, ResourceFactory factory, ResourceLayout resourceLayout)
            {
                _gd = gd;

                cameraInfoBuffer = factory.CreateBuffer(new BufferDescription(
                    (uint)Unsafe.SizeOf<GpuCameraInfo>(),
                    BufferUsage.UniformBuffer));
                resourceSet = factory.CreateResourceSet(new ResourceSetDescription(resourceLayout, cameraInfoBuffer));
            }

            public void UploadToGPU(CommandList cl)
            {
                cl.UpdateBuffer(cameraInfoBuffer, 0, cameraInfo);
            }

            public void Bind(CommandList cl)
            {
                cl.SetGraphicsResourceSet(0, resourceSet);
            }

            public void Update(Vector2 size)
            {
                Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
                    90.0f * (MathF.PI / 180.0f), 
                    size.X / size.Y, 
                    0.01f, 1000.0f);
                Matrix4x4 view = Matrix4x4.CreateLookAt(position, position + forward, up);

                cameraInfo = new GpuCameraInfo()
                {
                    vp = view * projection,
                    pos = new Vector4(position, 1.0f)
                };
            }

            public void Dispose()
            {
                _gd.WaitForIdle();

                cameraInfoBuffer.Dispose();
            }
        }

        private class Viewport : IDisposable
        {
            private GraphicsDevice _gd;
            private ResourceFactory _factory;
            private ImGuiRenderer _imguiRenderer;
            private Pipeline _pipeline;
            private CSGScene.Built _scene;

            private string name;

            public Camera camera;

            public Texture? tex = null;
            public nint texId = -1;
            public Texture? depthTex = null;
            public Framebuffer? framebuffer = null;

            private Vector2 _lastSize = Vector2.Zero;
            private bool _isMouseControllingViewport = false;
            private Vector2 _lastMousePos = Vector2.Zero;

            public float yaw = -90.0f;
            public float pitch = 0.0f;

            public Viewport(GraphicsDevice gd, ResourceFactory factory, ImGuiRenderer imguiRenderer, ResourceLayout resourceLayout, Pipeline pipeline, CSGScene.Built scene, string name)
            {
                _factory = factory;
                _gd = gd;
                _imguiRenderer = imguiRenderer;
                _pipeline = pipeline;
                _scene = scene;

                camera = new Camera(gd, factory, resourceLayout);
                this.name = name;
            }

            public void Resize(Vector2 size)
            {
                _gd.WaitForIdle();

                if (tex != null)
                {
                    _imguiRenderer.RemoveImGuiBinding(tex);
                    tex.Dispose();
                }
                depthTex?.Dispose();
                framebuffer?.Dispose();

                tex = _factory.CreateTexture(new TextureDescription(
                    (uint)size.X, (uint)size.Y, 1, 1, 1,
                    colorFormat,
                    TextureUsage.Sampled | TextureUsage.RenderTarget,
                    TextureType.Texture2D));
                depthTex = _factory.CreateTexture(new TextureDescription(
                    (uint)size.X, (uint)size.Y, 1, 1, 1,
                    depthFormat,
                    TextureUsage.DepthStencil,
                    TextureType.Texture2D));
                framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(
                    depthTex, tex));
                texId = _imguiRenderer.GetOrCreateImGuiBinding(_factory, tex);
            }

            public void UpdateAndRenderUI(float deltaTime)
            {
                Vector2 mousePos = ImGui.GetMousePos() - ImGui.GetWindowPos();
                Vector2 deltaMouse = mousePos - _lastMousePos;
                if (ImGui.IsWindowHovered())
                    _isMouseControllingViewport = ImGui.IsMouseDown(ImGuiMouseButton.Right);
                else
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                        _isMouseControllingViewport = false;
                }

                if (_isMouseControllingViewport)
                {
                    yaw += deltaMouse.X * _mouseSensitivity;
                    pitch += deltaMouse.Y * -_mouseSensitivity;

                    if (pitch > 89.0f)
                        pitch = 89.0f;
                    if (pitch < -89.0f)
                        pitch = -89.0f;
                }

                camera.forward.X =
                    MathF.Cos(MathF.PI * yaw / 180) *
                    MathF.Cos(MathF.PI * pitch / 180);
                camera.forward.Y =
                    MathF.Sin(MathF.PI * pitch / 180);
                camera.forward.Z =
                    MathF.Sin(MathF.PI * yaw / 180) *
                    MathF.Cos(MathF.PI * pitch / 180);
                camera.forward = Vector3.Normalize(camera.forward);

                var camRight = Vector3.Normalize(Vector3.Cross(camera.forward, Vector3.UnitY));
                camera.up = Vector3.Cross(camRight, camera.forward);

                if (ImGui.IsWindowHovered() || _isMouseControllingViewport)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.D))
                        camera.position += camRight * deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.A))
                        camera.position += -camRight * deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.W))
                        camera.position += camera.forward * deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.S))
                        camera.position += -camera.forward * deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.Space))
                        camera.position += Vector3.UnitY * deltaTime * 10.0f;
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                        camera.position += -Vector3.UnitY * deltaTime * 10.0f;
                }

                Vector2 windowSize = ImGui.GetWindowSize();
                if (windowSize.X > 1 && windowSize.Y > 1)
                {
                    if (windowSize != _lastSize)
                    {
                        Resize(windowSize);
                    }
                    _lastSize = windowSize;

                    ImGui.GetWindowDrawList().AddImage(texId, ImGui.GetWindowPos(), ImGui.GetWindowPos() + windowSize);
                }
                _lastMousePos = mousePos;

                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
                ImGui.TextUnformatted(name);
                ImGui.PopStyleColor();

                camera.Update(new Vector2(framebuffer.Width, framebuffer.Height));
            }

            public void Render(CommandList cl)
            {
                camera.UploadToGPU(cl);

                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new RgbaFloat(0.25f, 0.25f, 0.25f, 1.0f));
                cl.ClearDepthStencil(1);

                cl.SetPipeline(_pipeline);
                camera.Bind(cl);
                _scene.Draw(cl);
            }

            public void Dispose()
            {
                _gd.WaitForIdle();

                camera.Dispose();

                tex?.Dispose();
                depthTex?.Dispose();
                framebuffer?.Dispose();
            }
        }

        private Viewport _topLeftViewport;
        private Viewport _topRightViewport;
        private Viewport _bottomLeftViewport;
        private Viewport _bottomRightViewport;

        private ResourceLayout _resourceLayout;

        private Shader[] _shaders;
        private Pipeline? _pipeline = null;

        private CSGScene.Built _builtScene;

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

        public MainRenderer(CSGScene scene)
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

            _resourceLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ShaderDescription vertexShaderDesc = new(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(_vertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(_fragmentCode),
                "main");

            _shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            GraphicsPipelineDescription pipelineDescription = new();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);
            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);
            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleList;
            pipelineDescription.ResourceLayouts = new ResourceLayout[] { _resourceLayout };
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { CSGScene.vertexLayout },
                shaders: _shaders);
            pipelineDescription.Outputs = new OutputDescription(
                new OutputAttachmentDescription(depthFormat), 
                new OutputAttachmentDescription(colorFormat));
            _pipeline = _factory.CreateGraphicsPipeline(pipelineDescription);

            _cl = _gd.ResourceFactory.CreateCommandList();
            _imguiRenderer = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription,
                (int)_gd.MainSwapchain.Framebuffer.Width, (int)_gd.MainSwapchain.Framebuffer.Height);

            _builtScene = scene.Build(_gd, _factory);

            _topLeftViewport = new Viewport(_gd, _factory, _imguiRenderer, 
                _resourceLayout, _pipeline, _builtScene, "Top Left");
            _topRightViewport = new Viewport(_gd, _factory, _imguiRenderer, 
                _resourceLayout, _pipeline, _builtScene, "Top Right");
            _bottomLeftViewport = new Viewport(_gd, _factory, _imguiRenderer, 
                _resourceLayout, _pipeline, _builtScene, "Bottom Left");
            _bottomRightViewport = new Viewport(_gd, _factory, _imguiRenderer, 
                _resourceLayout, _pipeline, _builtScene, "Bottom Right");

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

        public void Render()
        {
            InputSnapshot input = _window.PumpEvents();

            ulong timeNS = (ulong)(((float)_deltaTimer.ElapsedTicks / Stopwatch.Frequency) * 1000000000.0);
            ulong deltaTimeNS = timeNS - _lastTimeNS;
            double deltaTime = deltaTimeNS / 1000000000.0;

            _imguiRenderer.Update((float)deltaTime, input);

            float windowWidth = _window.Width;
            float windowHeight = _window.Height;

            float sidebarWidth = windowWidth * 0.03f;

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
                _topLeftViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - sidebarWidth) / 2.0f, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth + ((windowWidth - sidebarWidth) / 2.0f), 0.0f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("TopRightViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _topRightViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - sidebarWidth) / 2.0f, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("BottomLeftViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _bottomLeftViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - sidebarWidth) / 2.0f, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth + ((windowWidth - sidebarWidth) / 2.0f), windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("BottomRightViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _bottomRightViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            _cl.Begin();
            {
                _topLeftViewport.Render(_cl);
                _topRightViewport.Render(_cl);
                _bottomLeftViewport.Render(_cl);
                _bottomRightViewport.Render(_cl);

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

                    _topLeftViewport.Dispose();
                    _topRightViewport.Dispose();
                    _bottomLeftViewport.Dispose();
                    _bottomRightViewport.Dispose();
                    
                    _imguiRenderer.Dispose();
                    _builtScene.Dispose();
                    _pipeline?.Dispose();
                    foreach (Shader shader in _shaders)
                        shader.Dispose();
                    _resourceLayout.Dispose();
                    _cl.Dispose();
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
