using CSGMan.Renderer;
using ImGuiNET;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Vulkan.Xlib;
using CSG;
using SharpGen.Runtime;
using System.Text;

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

        private CSGScene.Node? selectedNode = null;
        private byte[] nameInputBuf = new byte[256];

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

        private void RenderHierarchyNode(CSGScene.Node node, bool leaf, ImGuiTreeNodeFlags extraFlags = ImGuiTreeNodeFlags.None)
        {
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.OpenOnDoubleClick | extraFlags;
            if (leaf) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet;

            string operationIndicator = "";
            if (node is CSGBrush brush)
            {
                switch(brush.operation)
                {
                    case ShapeOperation.Union:
                        operationIndicator = " (U)";
                        break;
                    case ShapeOperation.Subtract:
                        operationIndicator = " (-)";
                        break;
                    case ShapeOperation.Intersect:
                        operationIndicator = " (I)";
                        break;
                }
            }

            var nodeOpen = ImGui.TreeNodeEx($"{node.name}{operationIndicator}", flags);
            if (ImGui.IsItemClicked() && node != _scene.root)
            {
                selectedNode = node;
                Array.Clear(nameInputBuf);
            }
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Add Cube"))
                {

                }

                if (ImGui.MenuItem("Delete"))
                {
                    if (selectedNode == node) selectedNode = null;
                    node.Remove();
                }

                ImGui.EndPopup();
            }
            if (nodeOpen)
            {
                // Have to copy it so it doesn't get modified in the middle of looping through it, which is invalid.
                CSGScene.Node[] childrenCopy = node.GetChildren().ToArray();
                foreach (CSGScene.Node child in childrenCopy)
                    RenderHierarchyNode(child, child.ChildCount == 0);
                ImGui.TreePop();
            }
        }

        private void RenderUI(float deltaTime)
        {
            float windowWidth = (float)_context.gd.MainSwapchain.Framebuffer.Width;
            float windowHeight = (float)_context.gd.MainSwapchain.Framebuffer.Height;

            float propertiesWidth = windowWidth * 0.17f;
            float hierarchyWidth = windowWidth * 0.17f;

            ImGui.BeginMainMenuBar();
            float mainMenuBarHeight = ImGui.GetWindowHeight();
            {
                if (ImGui.BeginMenu("File"))
                {
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    ImGui.EndMenu();
                }

                ImGui.EndMainMenuBar();
            }

            ImGui.SetNextWindowSize(new Vector2(propertiesWidth, windowHeight - mainMenuBarHeight), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(0, mainMenuBarHeight), ImGuiCond.Always);
            ImGui.Begin("Properties", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                if (selectedNode != null)
                {
                    ImGui.TextUnformatted($"{selectedNode.GetType().Name}");
                    ImGui.SameLine();

                    string inputLabel = "Name";
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(inputLabel).X - ImGui.GetStyle().WindowPadding.X);
                    Array.Copy(Encoding.Default.GetBytes(selectedNode.name), nameInputBuf, selectedNode.name.Length);
                    ImGui.InputText(inputLabel, nameInputBuf, 256);
                    int stringLength = Array.IndexOf(nameInputBuf, (byte)0);
                    stringLength = stringLength >= 0 ? stringLength : nameInputBuf.Length;
                    selectedNode.name = Encoding.Default.GetString(nameInputBuf, 0, stringLength);

                    if (selectedNode is CSGBrush selectedBrush)
                    {
                        Vector3 pos = selectedBrush.Position;
                        ImGui.DragFloat3("Position", ref pos, 0.1f);
                        selectedBrush.Position = pos;

                        Vector3 scale = selectedBrush.Scale;
                        ImGui.DragFloat3("Scale", ref scale, 0.1f);
                        selectedBrush.Scale = scale;
                    }
                }
                else ImGui.TextUnformatted("No Node Selected.");
            }
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2(hierarchyWidth, windowHeight - mainMenuBarHeight), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(windowWidth - hierarchyWidth, mainMenuBarHeight), ImGuiCond.Always);
            ImGui.Begin("Hierarchy", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                RenderHierarchyNode(_scene.root, false, ImGuiTreeNodeFlags.DefaultOpen);
            }
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, (windowHeight - mainMenuBarHeight) / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(propertiesWidth, mainMenuBarHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("TopLeftViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _topLeftViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, (windowHeight - mainMenuBarHeight) / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(propertiesWidth + (windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, mainMenuBarHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("TopRightViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _topRightViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, (windowHeight - mainMenuBarHeight) / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(propertiesWidth, ((windowHeight - mainMenuBarHeight) / 2.0f) + mainMenuBarHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("BottomLeftViewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove);
            {
                _bottomLeftViewport.UpdateAndRenderUI((float)deltaTime);
            }
            ImGui.PopStyleVar();
            ImGui.End();

            ImGui.SetNextWindowSize(new Vector2((windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, (windowHeight - mainMenuBarHeight) / 2.0f), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(propertiesWidth + (windowWidth - propertiesWidth - hierarchyWidth) / 2.0f, ((windowHeight - mainMenuBarHeight) / 2.0f) + mainMenuBarHeight), ImGuiCond.Always);
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
