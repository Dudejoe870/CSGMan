using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace CSGMan.Renderer
{
    public struct GpuCameraInfo
    {
        public Matrix4x4 vp;
        public Vector4 pos;
    }

    public class Camera
    {
        private GraphicsDevice _gd;

        public DeviceBuffer cameraInfoBuffer;
        public GpuCameraInfo cameraInfo = new();
        public ResourceSet resourceSet;

        public Vector3 position = new(0.0f, 0.0f, 4.0f);
        public Vector3 forward = -Vector3.UnitZ;
        public Vector3 up = Vector3.UnitY;

        public Camera(GraphicsDevice gd, ResourceFactory factory, ResourceLayout cameraResourceLayout)
        {
            _gd = gd;

            cameraInfoBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Unsafe.SizeOf<GpuCameraInfo>(),
                BufferUsage.UniformBuffer));
            resourceSet = factory.CreateResourceSet(
                new ResourceSetDescription(cameraResourceLayout, cameraInfoBuffer));
        }

        public void UploadToGPU(CommandList cl)
        {
            cl.UpdateBuffer(cameraInfoBuffer, 0, cameraInfo);
        }

        public void Bind(CommandList cl)
        {
            cl.SetGraphicsResourceSet(0, resourceSet);
        }

        public void Update(Vector2 size)
        {
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
                90.0f * (MathF.PI / 180.0f),
                size.X / size.Y,
                0.01f, 1000.0f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(position, position + forward, up);

            cameraInfo = new GpuCameraInfo()
            {
                vp = view * projection,
                pos = new Vector4(position, 1.0f)
            };
        }

        public void Dispose()
        {
            _gd.WaitForIdle();

            cameraInfoBuffer.Dispose();
        }
    }
}
