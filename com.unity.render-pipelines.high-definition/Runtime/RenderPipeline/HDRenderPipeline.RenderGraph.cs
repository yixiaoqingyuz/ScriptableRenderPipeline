using System;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class HDRenderPipeline
    {
        void ExecuteWithRenderGraph(RenderRequest renderRequest, ScriptableRenderContext renderContext, CommandBuffer cmd, DensityVolumeList densityVolumes)
        {
            var hdCamera = renderRequest.hdCamera;
            var camera = hdCamera.camera;
            var cullingResults = renderRequest.cullingResults.cullingResults;
            //var target = renderRequest.target;

            CreateSharedResources(m_RenderGraph, hdCamera, m_CurrentDebugDisplaySettings);

            StartStereoRendering(cmd, renderContext, camera);

            bool renderMotionVectorAfterGBuffer = RenderDepthPrepass(m_RenderGraph, cullingResults, hdCamera);

            if (!renderMotionVectorAfterGBuffer)
            {
                // If objects velocity if enabled, this will render the objects with motion vector into the target buffers (in addition to the depth)
                // Note: An object with motion vector must not be render in the prepass otherwise we can have motion vector write that should have been rejected
                RenderObjectsVelocityPass(m_RenderGraph, cullingResults, hdCamera);
            }

            // At this point in forward all objects have been rendered to the prepass (depth/normal/velocity) so we can resolve them
            RenderGraphResource depthValuesMSAA = ResolvePrepassBuffers(m_RenderGraph, hdCamera);

            /*
            // This will bind the depth buffer if needed for DBuffer)
            RenderDBuffer(hdCamera, cmd, renderContext, cullingResults);
            // We can call DBufferNormalPatch after RenderDBuffer as it only affect forward material and isn't affected by RenderGBuffer
            // This reduce lifteime of stencil bit
            DBufferNormalPatch(hdCamera, cmd, renderContext, cullingResults);

#if ENABLE_RAYTRACING
            bool raytracedIndirectDiffuse = m_RaytracingIndirectDiffuse.RenderIndirectDiffuse(hdCamera, cmd, renderContext, m_FrameCount);
            PushFullScreenDebugTexture(hdCamera, cmd, m_RaytracingIndirectDiffuse.GetIndirectDiffuseTexture(), FullScreenDebugMode.IndirectDiffuse);
            cmd.SetGlobalInt(HDShaderIDs._RaytracedIndirectDiffuse, raytracedIndirectDiffuse ? 1 : 0);
#endif
*/

            RenderGBuffer(m_RenderGraph, cullingResults, hdCamera);

            RenderGraphGlobalParams renderGraphParams = new RenderGraphGlobalParams();
            renderGraphParams.renderingViewport = hdCamera.renderingViewport;

            m_RenderGraph.Execute(renderContext, cmd, renderGraphParams);
        }

        static void DrawRendererList(RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (!rendererList.isValid)
                throw new ArgumentException("Invalid renderer list provided to DrawOpaqueRendererList");

            // This is done here because DrawRenderers API lives outside command buffers so we need to make call this before doing any DrawRenders
            renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            if (rendererList.stateBlock == null)
                renderContext.DrawRenderers(rendererList.cullingResult, ref rendererList.drawSettings, ref rendererList.filteringSettings);
            else
            {
                var renderStateBlock = rendererList.stateBlock.Value;
                renderContext.DrawRenderers(rendererList.cullingResult, ref rendererList.drawSettings, ref rendererList.filteringSettings, ref renderStateBlock);
            }
        }

        static void DrawOpaqueRendererList(in FrameSettings frameSettings, RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (!frameSettings.IsEnabled(FrameSettingsField.OpaqueObjects))
                return;

            DrawRendererList(rendererList, renderContext, cmd);
        }

        static void DrawTransparentRendererList(in FrameSettings frameSettings, RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (!frameSettings.IsEnabled(FrameSettingsField.TransparentObjects))
                return;

            DrawRendererList(rendererList, renderContext, cmd);
        }

        static int SampleCountToPassIndex(MSAASamples samples)
        {
            switch (samples)
            {
                case MSAASamples.None:
                    return 0;
                case MSAASamples.MSAA2x:
                    return 1;
                case MSAASamples.MSAA4x:
                    return 2;
                case MSAASamples.MSAA8x:
                    return 3;
            };
            return 0;
        }
    }
}
