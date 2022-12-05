using Veldrid.Sdl2;
using Veldrid;
using Veldrid.StartupUtilities;
using System.Diagnostics;
using ImGuiNET;
using System.Numerics;
using System.Text;
using Veldrid.SPIRV;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
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

        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;
        private static uint _indexCount;

        private static DeviceBuffer _cameraBuffer;

        private static ResourceSet _resourceSet;
        private static ResourceLayout _resourceLayout;

        private static Shader[] _shaders;
        private static Pipeline _pipeline;
        
        private static Vector3 _camPos;
        private static Vector3 _camFwd;

        private struct CameraInfo
        {
            public Matrix4x4 vp;
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

layout(location = 0) out flat vec4 fsin_Color;

layout(set = 0, binding = 0) uniform CameraBuffer
{
    mat4 vp;
};

void main()
{
    gl_Position = vp * vec4(Position, 1);
    fsin_Color = vec4(Normal, 1);
}";

        private const string _fragmentCode = @"
#version 460

layout(location = 0) in flat vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
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
                Debug = true,
                SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            });
            _factory = _gd.ResourceFactory;

            _cameraBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CameraInfo>(), 
                BufferUsage.UniformBuffer));

            var shape1 = new Sphere(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 16);
            var shape2 = new Cube(position: new Vector3(0, 0, 0), size: new Vector3(2.0f, 0.50f, 0.50f));
            var result = Shape.Subtract(shape1, shape2);

            _vertexBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)shape1.Vertices.Length * Vertex.SizeInBytes,
                BufferUsage.VertexBuffer));
            _indexBuffer = _factory.CreateBuffer(new BufferDescription(
                (uint)shape1.Indices.Length * sizeof(uint),
                BufferUsage.IndexBuffer));

            _gd.UpdateBuffer(_vertexBuffer, 0, shape1.Vertices);
            _gd.UpdateBuffer(_indexBuffer, 0, shape1.Indices);

            _indexCount = (uint)shape1.Indices.Length;

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
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);
            pipelineDescription.Outputs = _gd.SwapchainFramebuffer.OutputDescription;
            _pipeline = _factory.CreateGraphicsPipeline(pipelineDescription);

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

        public void Render()
        {
            InputSnapshot input = _window.PumpEvents();

            ulong timeNS = (ulong)(((float)_deltaTimer.ElapsedTicks / Stopwatch.Frequency) * 1000000000.0);
            ulong deltaTimeNS = timeNS - _lastTimeNS;
            double deltaTime = deltaTimeNS / 1000000000.0;

            _imguiRenderer.Update((float)deltaTime, input);

            if (ImGui.Begin("Camera"))
            {
                ImGui.DragFloat3("Position: ", ref _camPos, 0.1f);
                ImGui.DragFloat3("Forward: ", ref _camFwd, 0.1f);
            }
            ImGui.End();

            float width = _gd.MainSwapchain.Framebuffer.Width;
            float height = _gd.MainSwapchain.Framebuffer.Height;
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(90.0f * (MathF.PI / 180.0f), width / height, 0.01f, 1000.0f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(_camPos, _camPos + Vector3.Normalize(_camFwd), Vector3.UnitY);

            CameraInfo info = new()
            {
                vp = view * projection
            };

            _cl.Begin();
            {
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, RgbaFloat.Black);
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

                    _imguiRenderer.Dispose();
                    _pipeline.Dispose();
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
