using CSG;

namespace CSGMan
{
    public class CSGBrush : CSGScene.Node
    {
        public Shape shape;
        public Shape baseShape;
        public ShapeOperation operation;

        public CSGBrush(Shape baseShape, ShapeOperation operation)
        {
            this.baseShape = baseShape;
            this.operation = operation;

            shape = baseShape;
        }
    }
}
