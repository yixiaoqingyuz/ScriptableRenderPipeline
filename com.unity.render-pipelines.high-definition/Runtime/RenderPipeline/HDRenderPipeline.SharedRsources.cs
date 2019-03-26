using System;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class HDRenderPipeline
    {
        // The render target used when we do not support MSAA
        RenderGraphMutableResource  m_DepthBuffer;
        RenderGraphMutableResource  m_NormalBuffer;
        RenderGraphMutableResource  m_VelocityBuffer;

        RenderGraphMutableResource m_DepthBufferMipChain;
        HDUtils.PackedMipChainInfo m_DepthBufferMipChainInfo; // This is metadata

        // MSAA Render targets
        RenderGraphMutableResource  m_DepthBufferMSAA;
        RenderGraphMutableResource  m_NormalBufferMSAA;
        RenderGraphMutableResource  m_VelocityBufferMSAA;
        // This texture must be used because reading directly from an MSAA Depth buffer is way to expensive. The solution that we went for is writing the depth in an additional color buffer (10x cheaper to solve on ps4)
        RenderGraphMutableResource  m_DepthAsColorBufferMSAA;

        Vector2Int ComputeDepthBufferMipChainSize(Vector2Int screenSize)
        {
            m_DepthBufferMipChainInfo.ComputePackedMipChainInfo(screenSize);
            return m_DepthBufferMipChainInfo.textureSize;
        }

        public void InitializeSharedResources(HDRenderPipelineAsset hdAsset)
        {
            m_DepthBufferMipChainInfo = new HDUtils.PackedMipChainInfo();
            m_DepthBufferMipChainInfo.Allocate();
        }

        public void CleanupSharedResources()
        {
        }

        void CreateSharedResources(RenderGraph renderGraph, HDCamera hdCamera, DebugDisplaySettings debugDisplaySettings)
        {
            TextureDesc depthDesc = new TextureDesc(Vector2.one) { depthBufferBits = DepthBits.Depth32, clearBuffer = true, xrInstancing = true, useDynamicScale = true, name = "CameraDepthStencil" };

            m_DepthBuffer = renderGraph.CreateTexture(depthDesc);
            m_DepthBufferMSAA = renderGraph.CreateTexture(new TextureDesc(depthDesc) { bindTextureMS = true, enableMSAA = true, name = "CameraDepthStencilMSAA" });
            m_DepthAsColorBufferMSAA = renderGraph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R32_SFloat, clearBuffer = true, clearColor = Color.black, bindTextureMS = true, enableMSAA = true, xrInstancing = true, useDynamicScale = true, name = "DepthAsColorMSAA" }, HDShaderIDs._DepthTextureMS);
            m_DepthBufferMipChain = renderGraph.CreateTexture(new TextureDesc(ComputeDepthBufferMipChainSize) { colorFormat = GraphicsFormat.R32_SFloat, enableRandomWrite = true, xrInstancing = true, useDynamicScale = true, name = "CameraDepthBufferMipChain" }, HDShaderIDs._CameraDepthTexture);

            TextureDesc normalDesc = new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = NeedClearGBuffer(), clearColor = Color.black, xrInstancing = true, useDynamicScale = true, enableRandomWrite = true, name = "NormalBuffer" };
            m_NormalBuffer = renderGraph.CreateTexture(normalDesc, HDShaderIDs._NormalBufferTexture);
            m_NormalBufferMSAA = renderGraph.CreateTexture(new TextureDesc(normalDesc) { bindTextureMS = true, enableMSAA = true, enableRandomWrite = false, name = "NormalBufferMSAA" }, HDShaderIDs._NormalTextureMS);

            TextureDesc velocityDesc = new TextureDesc(Vector2.one) { colorFormat = Builtin.GetVelocityBufferFormat(), xrInstancing = true, useDynamicScale = true, name = "Velocity" };
            m_VelocityBuffer = renderGraph.CreateTexture(velocityDesc, HDShaderIDs._CameraMotionVectorsTexture);
            m_VelocityBufferMSAA = renderGraph.CreateTexture(new TextureDesc(velocityDesc) { bindTextureMS = true, enableMSAA = true, name = "VelocityMSAA" });

            m_IsDepthBufferCopyValid = false;
        }

        RenderGraphMutableResource GetDepthStencilBuffer(bool isMSAA = false)
        {
            return isMSAA ? m_DepthBufferMSAA : m_DepthBuffer;
        }

        RenderGraphMutableResource GetNormalBuffer(bool isMSAA = false)
        {
            return isMSAA ? m_NormalBufferMSAA : m_NormalBuffer;
        }

        RenderGraphMutableResource GetDepthTexture(bool isMSAA = false)
        {
            return isMSAA ? m_DepthAsColorBufferMSAA : m_DepthBufferMipChain;
        }

        RenderGraphMutableResource GetVelocityBuffer(bool isMSAA = false)
        {
            return isMSAA ? m_VelocityBufferMSAA : m_VelocityBuffer;
        }

        bool NeedClearColorBuffer(HDCamera hdCamera, SkyManager skyManager, DebugDisplaySettings debugDisplaySettings)
        {
            if (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Color ||
                // If the luxmeter is enabled, the sky isn't rendered so we clear the background color
                debugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter ||
                // If we want the sky but the sky don't exist, still clear with background color
                (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Sky && !skyManager.IsVisualSkyValid()) ||
                // Special handling for Preview we force to clear with background color (i.e black)
                // Note that the sky use in this case is the last one setup. If there is no scene or game, there is no sky use as reflection in the preview
                HDUtils.IsRegularPreviewCamera(hdCamera.camera)
                )
            {
                return true;
            }

            return false;
        }

        Color GetColorBufferClearColor(HDCamera hdCamera, DebugDisplaySettings debugDisplaySettings)
        {
            Color clearColor = hdCamera.backgroundColorHDR;
            // We set the background color to black when the luxmeter is enabled to avoid picking the sky color
            if (debugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter)
                clearColor = Color.black;

            return clearColor;
        }

        bool NeedClearGBuffer()
        {
            // TODO: Add an option to force clear
            return m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled();
        }

        HDUtils.PackedMipChainInfo GetDepthBufferMipChainInfo()
        {
            return m_DepthBufferMipChainInfo;
        }
    }
}
