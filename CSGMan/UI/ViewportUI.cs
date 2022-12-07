using CSGMan.Renderer;
using ImGuiNET;
using System.Numerics;
using Veldrid;

namespace CSGMan.UI
{
    public class ViewportUI : IDisposable
    {
        public string name;

        private const float _mouseSensitivity = 0.5f;

        private GraphicsDevice _gd;
        private ResourceFactory _factory;
        private ImGuiRenderer _imguiRenderer;
        private Pipeline _pipeline;
        private CSGScene.Built _scene;

        private Camera _camera;

        private Texture? _tex = null;
        private nint _texId = -1;
        private Texture? _depthTex = null;
        private Framebuffer? _framebuffer = null;

        private Vector2 _lastSize = Vector2.Zero;
        private bool _isMouseControllingViewport = false;
        private Vector2 _lastMousePos = Vector2.Zero;

        public float yaw = -90.0f;
        public float pitch = 0.0f;

        public ViewportUI(GraphicsDevice gd, ResourceFactory factory,
            ImGuiRenderer imguiRenderer,
            ResourceLayout cameraResourceLayout, Pipeline pipeline,
            CSGScene.Built scene, string name)
        {
            _factory = factory;
            _gd = gd;
            _imguiRenderer = imguiRenderer;
            _pipeline = pipeline;
            _scene = scene;

            _camera = new Camera(gd, factory, cameraResourceLayout);
            this.name = name;
        }

        public void Resize(Vector2 size)
        {
            _gd.WaitForIdle();

            if (_tex != null)
            {
                _imguiRenderer.RemoveImGuiBinding(_tex);
                _tex.Dispose();
            }
            _depthTex?.Dispose();
            _framebuffer?.Dispose();

            _tex = _factory.CreateTexture(new TextureDescription(
                (uint)size.X, (uint)size.Y, 1, 1, 1,
                MainRenderer.colorFormat,
                TextureUsage.Sampled | TextureUsage.RenderTarget,
                TextureType.Texture2D));
            _depthTex = _factory.CreateTexture(new TextureDescription(
                (uint)size.X, (uint)size.Y, 1, 1, 1,
                MainRenderer.depthFormat,
                TextureUsage.DepthStencil,
                TextureType.Texture2D));
            _framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(
                _depthTex, _tex));
            _texId = _imguiRenderer.GetOrCreateImGuiBinding(_factory, _tex);
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

            _camera.forward.X =
                MathF.Cos(MathF.PI * yaw / 180) *
                MathF.Cos(MathF.PI * pitch / 180);
            _camera.forward.Y =
                MathF.Sin(MathF.PI * pitch / 180);
            _camera.forward.Z =
                MathF.Sin(MathF.PI * yaw / 180) *
                MathF.Cos(MathF.PI * pitch / 180);
            _camera.forward = Vector3.Normalize(_camera.forward);

            var camRight = Vector3.Normalize(Vector3.Cross(_camera.forward, Vector3.UnitY));
            _camera.up = Vector3.Cross(camRight, _camera.forward);

            if (ImGui.IsWindowHovered() || _isMouseControllingViewport)
            {
                if (ImGui.IsKeyDown(ImGuiKey.D))
                    _camera.position += camRight * deltaTime * 10.0f;
                if (ImGui.IsKeyDown(ImGuiKey.A))
                    _camera.position += -camRight * deltaTime * 10.0f;
                if (ImGui.IsKeyDown(ImGuiKey.W))
                    _camera.position += _camera.forward * deltaTime * 10.0f;
                if (ImGui.IsKeyDown(ImGuiKey.S))
                    _camera.position += -_camera.forward * deltaTime * 10.0f;
                if (ImGui.IsKeyDown(ImGuiKey.Space))
                    _camera.position += Vector3.UnitY * deltaTime * 10.0f;
                if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    _camera.position += -Vector3.UnitY * deltaTime * 10.0f;
            }

            Vector2 windowSize = ImGui.GetWindowSize();
            if (windowSize.X > 1 && windowSize.Y > 1)
            {
                if (windowSize != _lastSize)
                {
                    Resize(windowSize);
                }
                _lastSize = windowSize;

                ImGui.GetWindowDrawList().AddImage(_texId, ImGui.GetWindowPos(), ImGui.GetWindowPos() + windowSize);
            }
            _lastMousePos = mousePos;

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
            ImGui.TextUnformatted(name);
            ImGui.PopStyleColor();

            _camera.Update(new Vector2(_framebuffer.Width, _framebuffer.Height));
        }

        public void Render(CommandList cl)
        {
            _camera.UploadToGPU(cl);

            cl.SetFramebuffer(_framebuffer);
            cl.ClearColorTarget(0, new RgbaFloat(0.25f, 0.25f, 0.25f, 1.0f));
            cl.ClearDepthStencil(1);

            cl.SetPipeline(_pipeline);
            _camera.Bind(cl);
            _scene.Draw(cl);
        }

        public void Dispose()
        {
            _gd.WaitForIdle();

            _camera.Dispose();

            _tex?.Dispose();
            _depthTex?.Dispose();
            _framebuffer?.Dispose();
        }
    }
}
