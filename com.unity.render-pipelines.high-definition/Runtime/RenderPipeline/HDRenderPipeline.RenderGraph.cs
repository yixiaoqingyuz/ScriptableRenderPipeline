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

            StartStereoRendering(m_RenderGraph, hdCamera.camera);

            var prepassOutput = RenderPrepass(m_RenderGraph, cullingResults, hdCamera);

            RenderCameraVelocity(m_RenderGraph, hdCamera);

            StopStereoRendering(m_RenderGraph, hdCamera.camera);

            var renderGraphParams = new RenderGraphExecuteParams()
            {
                renderingWidth = hdCamera.actualWidth,
                renderingHeight = hdCamera.actualHeight,
                msaaSamples = m_MSAASamples
            };

            //// Caution: We require sun light here as some skies use the sun light to render, it means that UpdateSkyEnvironment must be called after PrepareLightsForGPU.
            //// TODO: Try to arrange code so we can trigger this call earlier and use async compute here to run sky convolution during other passes (once we move convolution shader to compute).
            //UpdateSkyEnvironment(hdCamera, cmd);

            m_RenderGraph.Execute(renderContext, cmd, renderGraphParams);
        }

        protected static void DrawRendererList(RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
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

        protected static void DrawOpaqueRendererList(in FrameSettings frameSettings, RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (!frameSettings.IsEnabled(FrameSettingsField.OpaqueObjects))
                return;

            DrawRendererList(rendererList, renderContext, cmd);
        }

        protected static void DrawTransparentRendererList(in FrameSettings frameSettings, RendererList rendererList, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (!frameSettings.IsEnabled(FrameSettingsField.TransparentObjects))
                return;

            DrawRendererList(rendererList, renderContext, cmd);
        }

        protected static int SampleCountToPassIndex(MSAASamples samples)
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

        // XR Specific
        protected class StereoRenderingPassData : RenderPassData
        {
            public Camera camera;
        }

        protected void StartStereoRendering(RenderGraph renderGraph, Camera camera)
        {
            if (camera.stereoEnabled)
            {
                using (var builder = renderGraph.AddRenderPass<StereoRenderingPassData>("StartStereoRendering", out var passData))
                {
                    passData.camera = camera;
                    builder.SetRenderFunc(
                    (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                    {
                        StereoRenderingPassData stereoPassData = (StereoRenderingPassData)data;
                        // Reset scissor and viewport for C++ stereo code
                        renderGraphContext.cmd.DisableScissorRect();
                        renderGraphContext.cmd.SetViewport(stereoPassData.camera.pixelRect);
                        renderGraphContext.renderContext.ExecuteCommandBuffer(renderGraphContext.cmd);
                        renderGraphContext.cmd.Clear();
                        renderGraphContext.renderContext.StartMultiEye(stereoPassData.camera);
                    });
                }
            }
        }

        protected void StopStereoRendering(RenderGraph renderGraph, Camera camera)
        {
            if (camera.stereoEnabled)
            {
                using (var builder = renderGraph.AddRenderPass<StereoRenderingPassData>("StopStereoRendering", out var passData))
                {
                    passData.camera = camera;
                    builder.SetRenderFunc(
                    (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                    {
                        StereoRenderingPassData stereoPassData = (StereoRenderingPassData)data;
                        renderGraphContext.renderContext.ExecuteCommandBuffer(renderGraphContext.cmd);
                        renderGraphContext.cmd.Clear();
                        renderGraphContext.renderContext.StopMultiEye(stereoPassData.camera);
                    });
                }
            }
        }
    }
}
