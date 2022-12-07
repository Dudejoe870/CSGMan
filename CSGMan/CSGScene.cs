using CSG;
using CSG.Shapes;
using System.Numerics;
using Veldrid;

namespace CSGMan
{
    public class CSGScene
    {
        public class Node
        {
            public Node? parent = null;
            private List<Node> _children = new();

            public bool visible = true;

            public Node()
            {
            }

            public Node(Node parent)
            {
                this.parent = parent;
            }

            public void AddChild(Node node)
            {
                _children.Add(node);
                node.parent = this;
            }

            public IEnumerable<Node> GetChildren()
            {
                return _children;
            }
        }

        public Node root = new();

        // TODO: Only build the subtrees of the CSG Tree that were modified.
        private void BuildNode(Node node)
        {
            foreach (Node child in node.GetChildren())
                BuildNode(child);

            if (node.parent != null)
                if (node is CSGBrush childBrush && node.parent is CSGBrush parentBrush)
                    parentBrush.shape = parentBrush.shape.Do(childBrush.operation, childBrush.shape);
        }

        public Built Build(GraphicsDevice gd, ResourceFactory factory)
        {
            // Build the CSG Tree.
            BuildNode(root);

            // Combine all the top-level Meshes together into one Vertex and Index Buffer.
            List<Vertex> vertices = new();
            List<uint> indices = new();

            uint indexOffset = 0;
            foreach (Node child in root.GetChildren())
            {
                if (child is CSGBrush childBrush)
                {
                    vertices.AddRange(childBrush.shape.Vertices);
                    foreach (uint index in childBrush.shape.Indices)
                        indices.Add(index + indexOffset);
                    indexOffset += (uint)childBrush.shape.Vertices.Length;
                }
            }

            var vertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)vertices.Count * Vertex.SizeInBytes,
                BufferUsage.VertexBuffer));
            var indexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)indices.Count * sizeof(uint),
                BufferUsage.IndexBuffer));

            gd.UpdateBuffer(vertexBuffer, 0, vertices.ToArray());
            gd.UpdateBuffer(indexBuffer, 0, indices.ToArray());

            return new(new Built.Mesh(vertexBuffer, indexBuffer, (uint)indices.Count), gd);
        }

        public readonly static VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        public class Built : IDisposable
        {
            private GraphicsDevice _gd;

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

            public Mesh mesh;

            public Built(Mesh mesh, GraphicsDevice gd)
            {
                _gd = gd;

                this.mesh = mesh;
            }

            public void Draw(CommandList cl)
            {
                mesh.Draw(cl);
            }

            public void Dispose()
            {
                _gd.WaitForIdle();

                mesh.Dispose();
            }
        }
    }
}
