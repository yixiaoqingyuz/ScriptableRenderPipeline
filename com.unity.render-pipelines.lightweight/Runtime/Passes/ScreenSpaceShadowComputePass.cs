using System;
using UnityEngine.Experimental.VoxelizedShadows; //seongdae;vxsm
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

namespace UnityEngine.Experimental.Rendering.LWRP
{
    internal class ScreenSpaceShadowComputePass : ScriptableRenderPass
    {
        private static class VxShadowMapConstantBuffer
        {
            public static int _InvViewProjMatrixID;
            public static int _ScreenSizeID;
            public static int _VoxelZBiasID;
            public static int _VoxelUpBiasID;

            public static int _VxShadowMapsBufferID;
            public static int _ScreenSpaceShadowOutputID;
        }

        static readonly int TileSize = 8;
        static readonly int TileAdditive = TileSize - 1;

        const string k_CollectShadowsTag = "Collect Shadows";
        RenderTextureFormat m_ColorFormat;

        public ScreenSpaceShadowComputePass()
        {
            VxShadowMapConstantBuffer._InvViewProjMatrixID = Shader.PropertyToID("_InvViewProjMatrix");
            VxShadowMapConstantBuffer._ScreenSizeID = Shader.PropertyToID("_ScreenSize");
            VxShadowMapConstantBuffer._VoxelZBiasID = Shader.PropertyToID("_VoxelZBias");
            VxShadowMapConstantBuffer._VoxelUpBiasID = Shader.PropertyToID("_VoxelUpBias");

            VxShadowMapConstantBuffer._VxShadowMapsBufferID = Shader.PropertyToID("_VxShadowMapsBuffer");
            VxShadowMapConstantBuffer._ScreenSpaceShadowOutputID = Shader.PropertyToID("_ScreenSpaceShadowOutput");

            bool R8_UNorm = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.LoadStore);
            bool R8_SNorm = SystemInfo.IsFormatSupported(GraphicsFormat.R8_SNorm, FormatUsage.LoadStore);
            bool R8_UInt  = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UInt,  FormatUsage.LoadStore);
            bool R8_SInt  = SystemInfo.IsFormatSupported(GraphicsFormat.R8_SInt,  FormatUsage.LoadStore);
            
            bool R8 = R8_UNorm || R8_SNorm || R8_UInt || R8_SInt;
            
            m_ColorFormat = R8 ? RenderTextureFormat.R8 : RenderTextureFormat.RFloat;

            //Debug.Log("Screen Space Shadow Target format = " + m_ColorFormat);
        }

        private RenderTargetHandle colorAttachmentHandle { get; set; }
        private RenderTextureDescriptor descriptor { get; set; }
        private MainLightShadowCasterPass mainLightShadowCasterPass;
        private bool mainLightDynamicShadows = false;
        DirectionalVxShadowMap dirVxShadowMap;

        public void Setup(
            RenderTextureDescriptor baseDescriptor,
            RenderTargetHandle colorAttachmentHandle,
            MainLightShadowCasterPass mainLightShadowCasterPass,
            bool mainLightDynamicShadows)
        {
            this.colorAttachmentHandle = colorAttachmentHandle;

            baseDescriptor.autoGenerateMips = false;
            baseDescriptor.useMipMap = false;
            baseDescriptor.sRGB = false;
            baseDescriptor.depthBufferBits = 0;
            baseDescriptor.colorFormat = m_ColorFormat;
            baseDescriptor.enableRandomWrite = true;
            this.descriptor = baseDescriptor;
            this.mainLightShadowCasterPass = mainLightShadowCasterPass;
            this.mainLightDynamicShadows = mainLightDynamicShadows;
        }

        private int GetComputeShaderKernel(ref ComputeShader computeShader, ref ShadowData shadowData)
        {
            int kernel = -1;

            if (computeShader != null)
            {
                string blendModeName;
                if (mainLightDynamicShadows)
                    blendModeName = "BlendDynamicShadows";
                else
                    blendModeName = "NoBlend";

                string filteringName = "NoFilter";
                switch (shadowData.mainLightVxShadowQuality)
                {
                    case 1: filteringName = "Bilinear";  break;
                    case 2: filteringName = "Trilinear"; break;
                }

                string kernelName = blendModeName + filteringName;

                kernel = computeShader.FindKernel(kernelName);
            }

            return kernel;
        }

        public override void Execute(ScriptableRenderer renderer, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var computeShader = renderer.GetComputeShader(ComputeShaderHandle.ScreenSpaceShadow);
            int kernel = GetComputeShaderKernel(ref computeShader, ref renderingData.shadowData);
            if (kernel == -1)
                return;

            var shadowLightIndex = renderingData.lightData.mainLightIndex;
            var shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];

            var light = shadowLight.light;
            dirVxShadowMap = light.GetComponent<DirectionalVxShadowMap>();

            CommandBuffer cmd = CommandBufferPool.Get(k_CollectShadowsTag);

            cmd.GetTemporaryRT(colorAttachmentHandle.id, descriptor, FilterMode.Bilinear);

            if (mainLightDynamicShadows)
            {
                mainLightShadowCasterPass.SetMainLightShadowReceiverConstantsOnComputeShader(
                    cmd, ref renderingData.shadowData, shadowLight, computeShader);
            }

            SetupVxShadowReceiverConstants(
                cmd, kernel, ref computeShader, ref renderingData.cameraData.camera, ref shadowLight);

            int x = (renderingData.cameraData.camera.pixelWidth + TileAdditive) / TileSize;
            int y = (renderingData.cameraData.camera.pixelHeight + TileAdditive) / TileSize;

            cmd.DispatchCompute(computeShader, kernel, x, y, 1);

            // even if the main light doesn't have dynamic shadows,
            // cascades keyword is needed for screen space shadow map texture in opaque rendering pass.
            if (mainLightDynamicShadows == false)
            {
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, true);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, true);
            }
            else
            {
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, true);
            }

            if (renderingData.cameraData.isStereoEnabled)
            {
                Camera camera = renderingData.cameraData.camera;
                context.StartMultiEye(camera);
                context.ExecuteCommandBuffer(cmd);
                context.StopMultiEye(camera);
            }
            else
            {
                context.ExecuteCommandBuffer(cmd);
            }
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            if (colorAttachmentHandle != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(colorAttachmentHandle.id);
                colorAttachmentHandle = RenderTargetHandle.CameraTarget;
            }
        }

        void SetupVxShadowReceiverConstants(CommandBuffer cmd, int kernel, ref ComputeShader computeShader, ref Camera camera, ref VisibleLight shadowLight)
        {
            var light = shadowLight.light;

            float screenSizeX = (float)camera.pixelWidth;
            float screenSizeY = (float)camera.pixelHeight;
            float invScreenSizeX = 1.0f / screenSizeX;
            float invScreenSizeY = 1.0f / screenSizeY;

            var gpuView = camera.worldToCameraMatrix;
            var gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            var viewMatrix = gpuView;
            var projMatrix = gpuProj;
            var viewProjMatrix = projMatrix * viewMatrix;

            var vxShadowMapsBuffer = VxShadowMapsManager.instance.VxShadowMapsBuffer;

            int voxelZBias = dirVxShadowMap.voxelZBias;
            float voxelUpBias = dirVxShadowMap.voxelUpBias * (dirVxShadowMap.volumeScale / dirVxShadowMap.voxelResolutionInt);

            cmd.SetComputeMatrixParam(computeShader, VxShadowMapConstantBuffer._InvViewProjMatrixID, viewProjMatrix.inverse);
            cmd.SetComputeVectorParam(computeShader, VxShadowMapConstantBuffer._ScreenSizeID, new Vector4(screenSizeX, screenSizeY, invScreenSizeX, invScreenSizeY));
            cmd.SetComputeIntParam(computeShader, VxShadowMapConstantBuffer._VoxelZBiasID, voxelZBias);
            cmd.SetComputeFloatParam(computeShader, VxShadowMapConstantBuffer._VoxelUpBiasID, voxelUpBias);

            cmd.SetComputeBufferParam(computeShader, kernel, VxShadowMapConstantBuffer._VxShadowMapsBufferID, vxShadowMapsBuffer);
            cmd.SetComputeTextureParam(computeShader, kernel, VxShadowMapConstantBuffer._ScreenSpaceShadowOutputID, colorAttachmentHandle.Identifier());
        }
    }
}
