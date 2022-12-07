using CSG.Shapes;
using CSG;
using System.Numerics;

namespace CSGMan
{
    internal class Program
    {
        public static void Main()
        {
            CSGScene scene = new();
            {
                Cylinder shape1 = new(start: new Vector3(0, 2, 0), end: new Vector3(0, -2, 0), radius: 2, tessellation: 16);
                Cube shape2 = new(position: new Vector3(0, 0, 0), size: new Vector3(2, 1, 1));
                Cube shape3 = new(position: new Vector3(0, 0, 0), size: new Vector3(3, 2, 3));

                /*
                 * shape3 - (shape1 U shape2)
                 */
                CSGBrush brush1 = new(shape3, ShapeOperation.Union);
                scene.root.AddChild(brush1);
                CSGBrush brush2 = new(shape1, ShapeOperation.Subtract);
                brush1.AddChild(brush2);
                CSGBrush brush3 = new(shape2, ShapeOperation.Union);
                brush2.AddChild(brush3);
            }

            MainRenderer mainRenderer = new(scene);

            while (!mainRenderer.ShouldStop())
            {
                mainRenderer.Render();
            }

            mainRenderer.Dispose();
        }
    }
}
