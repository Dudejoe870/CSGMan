using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace CSGMan.Renderer
{
    public class GraphicsContext : IDisposable
    {
        public GraphicsDevice gd;
        public Sdl2Window window;
        public ResourceFactory factory;

        public ResourceLayout cameraResourceLayout;

        public GraphicsContext()
        {
            window = VeldridStartup.CreateWindow(new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "CSGMan"
            });

            gd = VeldridStartup.CreateGraphicsDevice(window, new GraphicsDeviceOptions()
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                ResourceBindingModel = ResourceBindingModel.Improved,
                Debug = true
            });
            factory = gd.ResourceFactory;

            cameraResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            window.Resized += () =>
            {
                gd.ResizeMainWindow((uint)window.Width, (uint)window.Height);
            };
        }

        public void Dispose()
        {
            gd.WaitForIdle();

            cameraResourceLayout.Dispose();
            gd.Dispose();
        }
    }
}
