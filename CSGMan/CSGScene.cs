using CSG;
using CSGMan.Renderer;
using System.Diagnostics;
using Veldrid;

namespace CSGMan
{
    public class CSGScene : IDisposable
    {
        public class Node
        {
            public string name;

            public Node? Parent { get; private set; }
            private List<Node> _children = new();

            public int ChildCount => _children.Count;

            public bool Invalidated { get; private set; }

            public bool Visible { get; private set; }

            public Node(string name)
            {
                this.name = name;
                Visible = true;
            }

            public void SetVisible(bool visible)
            {
                if (Visible != visible)
                    Invalidate();
                Visible = visible;
            }

            public void Invalidate()
            {
                Invalidated = true;
                OnInvalidate();
                if (Parent != null && !Parent.Invalidated)
                    Parent.Invalidate();
            }

            public virtual void OnInvalidate() { }

            public void Validate()
            {
                Invalidated = false;
            }

            public void AddChild(Node node)
            {
                _children.Add(node);
                node.Parent = this;
                node.Invalidate();
            }

            public void Remove()
            {
                Parent?._children.Remove(this);

                // Have to copy it so it doesn't get modified in
                // the middle of looping through it, which is invalid.
                Node[] childrenCopy = _children.ToArray();
                foreach (Node child in childrenCopy)
                    child.Remove();
                Parent?.Invalidate();
            }

            public IEnumerable<Node> GetChildren()
            {
                return _children;
            }
        }

        public class RootNode : Node
        {
            private CSGScene _scene;

            public RootNode(CSGScene scene)
                : base("root")
            {
                _scene = scene;
            }

            public override void OnInvalidate()
            {
                _scene.Invalidate();
            }
        }

        public RootNode root;

        private GraphicsContext _context;

        public CSGScene(GraphicsContext context)
        {
            _context = context;

            root = new RootNode(this);
        }

        private Built? _builtScene = null;
        public Built BuiltScene => _builtScene ??= Build();

        public void Invalidate()
        {
            _builtScene?.Dispose();
            _builtScene = null;
        }

        private void BuildNode(Node node)
        {
            if (node.Invalidated && node.Visible)
            {
                if (node is CSGBrush brush)
                    brush.shape = brush.baseShape;

                foreach (Node child in node.GetChildren())
                    BuildNode(child);
                node.Validate();
            }

            if (node.Parent != null)
                if (node is CSGBrush childBrush && node.Parent is CSGBrush parentBrush)
                    parentBrush.shape = parentBrush.shape.Do(childBrush.operation, childBrush.shape);
        }

        public Built Build()
        {
            try
            {
                // Build the CSG Tree.
                BuildNode(root);
            } 
            catch
            {
                Console.WriteLine("!!!");
                return new(_context);
            }

            // Combine all the top-level Meshes together into one Vertex and Index Buffer.
            List<Vertex> vertices = new();
            List<uint> indices = new();

            uint indexOffset = 0;
            foreach (Node child in root.GetChildren())
            {
                if (child.Visible && child is CSGBrush childBrush)
                {
                    vertices.AddRange(childBrush.shape.Vertices);
                    foreach (uint index in childBrush.shape.Indices)
                        indices.Add(index + indexOffset);
                    indexOffset += (uint)childBrush.shape.Vertices.Length;
                }
            }

            if (indices.Count == 0)
                return new(_context);

            var vertexBuffer = _context.factory.CreateBuffer(new BufferDescription(
                (uint)vertices.Count * Vertex.SizeInBytes,
                BufferUsage.VertexBuffer));
            var indexBuffer = _context.factory.CreateBuffer(new BufferDescription(
                (uint)indices.Count * sizeof(uint),
                BufferUsage.IndexBuffer));

            _context.gd.UpdateBuffer(vertexBuffer, 0, vertices.ToArray());
            _context.gd.UpdateBuffer(indexBuffer, 0, indices.ToArray());

            return new(new Built.Mesh(vertexBuffer, indexBuffer, (uint)indices.Count), _context);
        }

        public void Dispose()
        {
            _builtScene?.Dispose();
        }

        public readonly static VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        public class Built : IDisposable
        {
            private GraphicsContext _context;

            // TODO: There will be a single Mesh per texture.
            // Each Polygon will have its own texture ID,
            // and thus will be put into its own Mesh along with all other Polygons with that ID.
            public struct Mesh : IDisposable
            {
                public DeviceBuffer vertexBuffer;
                public DeviceBuffer indexBuffer;
                public uint indexCount;

                public Mesh(DeviceBuffer vertexBuffer, DeviceBuffer indexBuffer, uint indexCount)
                {
                    this.vertexBuffer = vertexBuffer;
                    this.indexBuffer = indexBuffer;
                    this.indexCount = indexCount;
                }

                public void Draw(CommandList cl)
                {
                    cl.SetVertexBuffer(0, vertexBuffer);
                    cl.SetIndexBuffer(indexBuffer, IndexFormat.UInt32);
                    cl.DrawIndexed(indexCount);
                }

                public void Dispose()
                {
                    vertexBuffer.Dispose();
                    indexBuffer.Dispose();
                }
            }

            public Mesh? mesh = null;

            public Built(GraphicsContext context)
            {
                _context = context;
            }

            public Built(Mesh mesh, GraphicsContext context)
            {
                _context = context;

                this.mesh = mesh;
            }

            public void Draw(CommandList cl)
            {
                mesh?.Draw(cl);
            }

            public void Dispose()
            {
                _context.gd.WaitForIdle();

                mesh?.Dispose();
            }
        }
    }
}
