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

            var prepassOutput = RenderPrepass(m_RenderGraph, cullingResults, hdCamera);

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
