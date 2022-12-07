using Veldrid.Sdl2;
using Veldrid;
using Veldrid.StartupUtilities;
using System.Diagnostics;
using System.Text;
using Veldrid.SPIRV;
using CSGMan.UI;

namespace CSGMan.Renderer
{
    public class MainRenderer : IDisposable
    {
        public const PixelFormat colorFormat = PixelFormat.R8_G8_B8_A8_UNorm;
        public const PixelFormat depthFormat = PixelFormat.D24_UNorm_S8_UInt;

        private GraphicsContext _context;

        private CSGScene _scene;

        private CommandList _cl;

        private Shader[] _shaders;
        private Pipeline _pipeline;

        private MainUI _mainUI;

        private ulong _lastTimeNS = 0;
        private Stopwatch _deltaTimer = new();

        public bool IsDisposed { get; private set; }

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

        public MainRenderer(GraphicsContext context, CSGScene scene)
        {
            _context = context;
            _scene = scene;

            ShaderDescription vertexShaderDesc = new(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(_vertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(_fragmentCode),
                "main");

            _shaders = context.factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

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
            pipelineDescription.ResourceLayouts = new ResourceLayout[] { _context.cameraResourceLayout };
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { CSGScene.vertexLayout },
                shaders: _shaders);
            pipelineDescription.Outputs = new OutputDescription(
                new OutputAttachmentDescription(depthFormat),
                new OutputAttachmentDescription(colorFormat));
            _pipeline = context.factory.CreateGraphicsPipeline(pipelineDescription);

            _cl = context.gd.ResourceFactory.CreateCommandList();

            _mainUI = new MainUI(context, _pipeline, scene);

            _deltaTimer.Start();
        }

        public bool ShouldStop()
        {
            return !_context.window.Exists;
        }

        public void Render()
        {
            InputSnapshot input = _context.window.PumpEvents();

            ulong timeNS = (ulong)((float)_deltaTimer.ElapsedTicks / Stopwatch.Frequency * 1000000000.0);
            ulong deltaTimeNS = timeNS - _lastTimeNS;
            double deltaTime = deltaTimeNS / 1000000000.0;
            _mainUI.Update((float)deltaTime, input);

            _cl.Begin();
            {
                _mainUI.Render(_cl);
            }
            _cl.End();

            _context.gd.SubmitCommands(_cl);

            // Technically this is inefficient.
            // However this is just a tool, so I'm not going to concern my self with optimal API usage.
            // I'd rather actually get it done.
            _context.gd.WaitForIdle();

            _context.gd.SwapBuffers();

            _lastTimeNS = timeNS;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _context.gd.WaitForIdle();

                    _mainUI.Dispose();

                    _scene.Dispose();
                    _pipeline?.Dispose();
                    foreach (Shader shader in _shaders)
                        shader.Dispose();
                    _cl.Dispose();
                }

                IsDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
