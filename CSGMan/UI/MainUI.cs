using ImGuiNET;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;

namespace CSGMan.UI
{
    public class MainUI : IDisposable
    {
        private GraphicsDevice _gd;
        private ResourceFactory _factory;
        private Sdl2Window _window;

        private CSGScene _scene;

        private Pipeline _pipeline;
        private CSGScene.Built _builtScene;

        private ImGuiRenderer _imguiRenderer;

        private ViewportUI _topLeftViewport;
        private ViewportUI _topRightViewport;
        private ViewportUI _bottomLeftViewport;
        private ViewportUI _bottomRightViewport;

        public bool IsDisposed { get; private set; }

        public MainUI(GraphicsDevice gd, ResourceFactory factory,
            ResourceLayout cameraResourceLayout,
            Pipeline pipeline, CSGScene scene,
            CSGScene.Built builtScene,
            Sdl2Window window)
        {
            _gd = gd;
            _factory = factory;
            _scene = scene;
            _pipeline = pipeline;
            _builtScene = builtScene;
            _window = window;

            _imguiRenderer = new ImGuiRenderer(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription,
                (int)_gd.MainSwapchain.Framebuffer.Width, (int)_gd.MainSwapchain.Framebuffer.Height);

            _topLeftViewport = new ViewportUI(gd, factory, _imguiRenderer,
                cameraResourceLayout, pipeline, builtScene, "Top Left");
            _topRightViewport = new ViewportUI(gd, factory, _imguiRenderer,
                cameraResourceLayout, pipeline, builtScene, "Top Right");
            _bottomLeftViewport = new ViewportUI(gd, factory, _imguiRenderer,
                cameraResourceLayout, pipeline, builtScene, "Bottom Left");
            _bottomRightViewport = new ViewportUI(gd, factory, _imguiRenderer,
                cameraResourceLayout, pipeline, builtScene, "Bottom Right");

            window.Resized += () =>
            {
                _imguiRenderer.WindowResized(
                    (int)_gd.MainSwapchain.Framebuffer.Width,
                    (int)_gd.MainSwapchain.Framebuffer.Height);
            };
        }

        public void Update(float deltaTime, InputSnapshot input)
        {
            _imguiRenderer.Update(deltaTime, input);
            RenderUI(deltaTime);
        }

        private void RenderUI(float deltaTime)
        {
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

            cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            _imguiRenderer.Render(_gd, cl);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _gd.WaitForIdle();

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
