using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace CSGMan
{
    internal class Program
    {
        public static void Main()
        {
            MainRenderer mainRenderer = new();

            while (!mainRenderer.ShouldStop())
            {
                mainRenderer.Render();
            }

            mainRenderer.Dispose();
        }
    }
}
