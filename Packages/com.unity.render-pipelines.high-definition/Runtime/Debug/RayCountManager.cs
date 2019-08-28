using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.HighDefinition
{
    class RayCountManager
    {
        // Indices of the values that we can query
        [GenerateHLSL]
        public enum RayCountValues
        {
            Visibility = 0,
            Indirect = 1,
            Forward = 2,
            GBuffer = 3,
            Count = 4,
            Total = 5
        }
#if ENABLE_RAYTRACING
        // Texture that holds the ray count per pixel
        RTHandle m_RayCountTexture = null;

        // Buffer that holds the reductions of the ray count
        ComputeBuffer m_ReducedRayCountBuffer0 = null;
        ComputeBuffer m_ReducedRayCountBuffer1 = null;
        ComputeBuffer m_ReducedRayCountBuffer2 = null;

        // CPU Buffer that holds the current values
        uint[] m_ReducedRayCountValues = new uint[(int)RayCountValues.Count];

        // HDRP Resources
        ComputeShader rayCountCS;

        // Flag that defines if ray counting is enabled for the current frame
        bool m_IsActive;

        // Given that the requests are guaranteed to be executed in order we use a queue to store it
        Queue<AsyncGPUReadbackRequest> rayCountReadbacks = new Queue<AsyncGPUReadbackRequest>();

        public void Init(HDRenderPipelineRayTracingResources rayTracingResources, DebugDisplaySettings currentDebugDisplaySettings)
        {
            // Keep track of the compute shader we are going to use
            rayCountCS = rayTracingResources.countTracedRays;

            // Allocate the texture that will hold the ray count
            m_RayCountTexture = RTHandles.Alloc(Vector2.one, filterMode: FilterMode.Point, colorFormat: GraphicsFormat.R16G16B16A16_UInt, enableRandomWrite: true, useMipMap: false, name: "RayCountTextureDebug");

            // We only require 3 buffers (this supports a maximal size of 8192x8192)
            m_ReducedRayCountBuffer0 = new ComputeBuffer((int)RayCountValues.Count * 256 * 256, sizeof(uint));
            m_ReducedRayCountBuffer1 = new ComputeBuffer((int)RayCountValues.Count * 32 * 32, sizeof(uint));
            m_ReducedRayCountBuffer2 = new ComputeBuffer((int)RayCountValues.Count + 1, sizeof(uint));

            // Initialize the CPU  ray count (Optional)
            for(int i = 0; i < (int)RayCountValues.Count; ++i)
            {
                m_ReducedRayCountValues[i] = 0;
            }

            // By default, this is not active
            m_IsActive = false;
        }

        public void Release()
        {
            RTHandles.Release(m_RayCountTexture);
            CoreUtils.SafeRelease(m_ReducedRayCountBuffer0);
            CoreUtils.SafeRelease(m_ReducedRayCountBuffer1);
            CoreUtils.SafeRelease(m_ReducedRayCountBuffer2);
        }

        public void ClearRayCount(CommandBuffer cmd, HDCamera camera, bool isActive)
        {
            m_IsActive = isActive;

            // Make sure to clear before the current frame
            if (m_IsActive)
            {
                // Grab the kernel that we will be using for the clear
                int currentKenel = rayCountCS.FindKernel("ClearBuffer");

                // We only clear the 256x256 texture, the clear will then implicitly propagate to the lower resolutions
                cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer0);
                cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 256);
                int tileSize = 256 / 32;
                cmd.DispatchCompute(rayCountCS, currentKenel, tileSize, tileSize, 1);

                // Clear the ray count texture (that ensures that we don't have to check what we are reading while we reduce)
                CoreUtils.SetRenderTarget(cmd, m_RayCountTexture, ClearFlag.Color);
            }
        }

        public int RayCountIsEnabled()
        {
            return m_IsActive ? 1 : 0;
        }

        public RTHandle GetRayCountTexture()
        {
            return m_RayCountTexture;
        }

        public void EvaluateRayCount(CommandBuffer cmd, HDCamera camera)
        {
            if (m_IsActive)
            {
                using (new ProfilingSample(cmd, "Raytracing Debug Overlay", CustomSamplerId.RaytracingDebug.GetSampler()))
                {
                    // Get the size of the viewport to process
                    int currentWidth = camera.actualWidth;
                    int currentHeight = camera.actualHeight;

                    // Grab the kernel that we will be using for the reduction
                    int currentKenel = rayCountCS.FindKernel("TextureReduction");

                    // Compute the dispatch dimensions
                    int areaTileSize = 32;
                    int dispatchWidth = Mathf.Max(1, (currentWidth + (areaTileSize - 1)) / areaTileSize);
                    int dispatchHeight = Mathf.Max(1, (currentHeight + (areaTileSize - 1)) / areaTileSize);

                    // Do we need three passes
                    if (dispatchHeight > 32  || dispatchWidth > 32)
                    {
                        // Bind the texture and the 256x256 buffer
                        cmd.SetComputeTextureParam(rayCountCS, currentKenel, HDShaderIDs._InputRayCountTexture, m_RayCountTexture);
                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer0);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 256);
                        cmd.DispatchCompute(rayCountCS, currentKenel, dispatchWidth, dispatchHeight, 1);

                        // Let's move to the next reduction pass
                        currentWidth /= 32;
                        currentHeight /= 32;

                        // Grab the kernel that we will be using for the reduction
                        currentKenel = rayCountCS.FindKernel("BufferReduction");

                        // Compute the dispatch dimensions
                        dispatchWidth = Mathf.Max(1, (currentWidth + (areaTileSize - 1)) / areaTileSize);
                        dispatchHeight = Mathf.Max(1, (currentHeight + (areaTileSize - 1)) / areaTileSize);

                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._InputRayCountBuffer, m_ReducedRayCountBuffer0);
                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer1);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._InputBufferDimension, 256);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 32);
                        cmd.DispatchCompute(rayCountCS, currentKenel, dispatchWidth, dispatchHeight, 1);

                        // Let's move to the next reduction pass
                        currentWidth /= 32;
                        currentHeight /= 32;

                        // Compute the dispatch dimensions
                        dispatchWidth = Mathf.Max(1, (currentWidth + (areaTileSize - 1)) / areaTileSize);
                        dispatchHeight = Mathf.Max(1, (currentHeight + (areaTileSize - 1)) / areaTileSize);

                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._InputRayCountBuffer, m_ReducedRayCountBuffer1);
                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer2);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._InputBufferDimension, 32);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 1);
                        cmd.DispatchCompute(rayCountCS, currentKenel, dispatchWidth, dispatchHeight, 1);
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(rayCountCS, currentKenel, HDShaderIDs._InputRayCountTexture, m_RayCountTexture);
                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer1);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 32);
                        cmd.DispatchCompute(rayCountCS, currentKenel, dispatchWidth, dispatchHeight, 1);

                        // Let's move to the next reduction pass
                        currentWidth /= 32;
                        currentHeight /= 32;

                        // Grab the kernel that we will be using for the reduction
                        currentKenel = rayCountCS.FindKernel("BufferReduction");

                        // Compute the dispatch dimensions
                        dispatchWidth = Mathf.Max(1, (currentWidth + (areaTileSize - 1)) / areaTileSize);
                        dispatchHeight = Mathf.Max(1, (currentHeight + (areaTileSize - 1)) / areaTileSize);

                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._InputRayCountBuffer, m_ReducedRayCountBuffer1);
                        cmd.SetComputeBufferParam(rayCountCS, currentKenel, HDShaderIDs._OutputRayCountBuffer, m_ReducedRayCountBuffer2);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._InputBufferDimension, 32);
                        cmd.SetComputeIntParam(rayCountCS, HDShaderIDs._OutputBufferDimension, 1);
                        cmd.DispatchCompute(rayCountCS, currentKenel, dispatchWidth, dispatchHeight, 1);
                    }

                    // Enqueue an Async read-back for the single value
                    AsyncGPUReadbackRequest singleReadBack = AsyncGPUReadback.Request(m_ReducedRayCountBuffer2, (int)RayCountValues.Count * sizeof(uint), 0);
                    rayCountReadbacks.Enqueue(singleReadBack);

                }
            }
        }

        public float GetRaysPerFrame(RayCountValues rayCountValue)
        {
            if (!m_IsActive)
            {
                return 0.0f;
            }
            else
            {
                while(rayCountReadbacks.Peek().done || rayCountReadbacks.Peek().hasError ==  true)
                {
                    // If this has an error, just skip it
                    if (!rayCountReadbacks.Peek().hasError)
                    {
                        // Grab the native array from this readback
                        NativeArray<uint> sampleCount = rayCountReadbacks.Peek().GetData<uint>();
                        for(int i = 0; i < (int)RayCountValues.Count; ++i)
                        {
                            m_ReducedRayCountValues[i] = sampleCount[i];
                        }
                    }
                    rayCountReadbacks.Dequeue();
                }

                if (rayCountValue != RayCountValues.Total)
                {
                    return m_ReducedRayCountValues[(int)rayCountValue];
                }
                else
                {
                    return m_ReducedRayCountValues[(int)RayCountValues.Visibility]
                        + m_ReducedRayCountValues[(int)RayCountValues.Indirect]
                        + m_ReducedRayCountValues[(int)RayCountValues.GBuffer]
                        + m_ReducedRayCountValues[(int)RayCountValues.Forward];
                }
            }
        }
#endif
    }
}
