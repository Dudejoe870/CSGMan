using CSGMan.Renderer;
using ImGuiNET;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Vulkan.Xlib;

namespace CSGMan.UI
{
    public class MainUI : IDisposable
    {
        private GraphicsContext _context;

        private CSGScene _scene;

        private Pipeline _pipeline;

        private ImGuiRenderer _imguiRenderer;

        private ViewportUI _topLeftViewport;
        private ViewportUI _topRightViewport;
        private ViewportUI _bottomLeftViewport;
        private ViewportUI _bottomRightViewport;

        public bool IsDisposed { get; private set; }

        public MainUI(GraphicsContext context, Pipeline pipeline, CSGScene scene)
        {
            _context = context;

            _scene = scene;
            _pipeline = pipeline;
            _scene = scene;

            _imguiRenderer = new ImGuiRenderer(context.gd, context.gd.MainSwapchain.Framebuffer.OutputDescription,
                (int)context.gd.MainSwapchain.Framebuffer.Width, (int)context.gd.MainSwapchain.Framebuffer.Height);

            _topLeftViewport = new ViewportUI(context, _imguiRenderer, pipeline, scene, "Top Left");
            _topRightViewport = new ViewportUI(context, _imguiRenderer, pipeline, scene, "Top Right");
            _bottomLeftViewport = new ViewportUI(context, _imguiRenderer, pipeline, scene, "Bottom Left");
            _bottomRightViewport = new ViewportUI(context, _imguiRenderer, pipeline, scene, "Bottom Right");

            context.window.Resized += () =>
            {
                _imguiRenderer.WindowResized(
                    (int)context.gd.MainSwapchain.Framebuffer.Width,
                    (int)context.gd.MainSwapchain.Framebuffer.Height);
            };
        }

        public void Update(float deltaTime, InputSnapshot input)
        {
            _imguiRenderer.Update(deltaTime, input);
            RenderUI(deltaTime);
        }

        private void RenderUI(float deltaTime)
        {
            float windowWidth = _context.window.Width;
            float windowHeight = _context.window.Height;

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
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth + (windowWidth - sidebarWidth) / 2.0f, 0.0f), ImGuiCond.Always);
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
            ImGui.SetNextWindowPos(new Vector2(sidebarWidth + (windowWidth - sidebarWidth) / 2.0f, windowHeight / 2.0f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("BottomRightViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _bottomRightViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();
        }

        public void Render(CommandList cl)
        {
            _topLeftViewport.Render(cl);
            _topRightViewport.Render(cl);
            _bottomLeftViewport.Render(cl);
            _bottomRightViewport.Render(cl);

            cl.SetFramebuffer(_context.gd.MainSwapchain.Framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            _imguiRenderer.Render(_context.gd, cl);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _context.gd.WaitForIdle();

                    _topLeftViewport.Dispose();
                    _topRightViewport.Dispose();
                    _bottomLeftViewport.Dispose();
                    _bottomRightViewport.Dispose();

                    _imguiRenderer.Dispose();
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
