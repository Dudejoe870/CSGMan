using CSG;
using System.ComponentModel;
using System.Numerics;

namespace CSGMan
{
    public class CSGBrush : CSGScene.Node
    {
        public Shape shape;
        public Shape baseShape;
        public ShapeOperation operation;

        public Vector3 Position
        {
            get => baseShape.Position;
            set
            {
                if (baseShape.Position != value)
                {
                    baseShape.Position = value;
                    Invalidate();
                }
            }
        }

        public Vector3 Scale
        {
            get => baseShape.Scale;
            set
            {
                if (baseShape.Scale != value)
                {
                    baseShape.Scale = value;
                    Invalidate();
                }
            }
        }

        public CSGBrush(string name, Shape baseShape, ShapeOperation operation)
            : base(name)
        {
            this.baseShape = baseShape;
            this.operation = operation;

            shape = baseShape;
        }
    }
}
