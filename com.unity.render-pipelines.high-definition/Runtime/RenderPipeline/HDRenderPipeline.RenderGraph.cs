using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    using TextureDesc = RenderGraphResourceRegistry.TextureDesc;

    public partial class HDRenderPipeline
    {
        void ExecuteWithRenderGraph(RenderRequest renderRequest, ScriptableRenderContext renderContext, CommandBuffer cmd, DensityVolumeList densityVolumes)
        {
            var hdCamera = renderRequest.hdCamera;
            var camera = hdCamera.camera;
            var cullingResults = renderRequest.cullingResults.cullingResults;
            //var target = renderRequest.target;

            bool msaa = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);
            bool clearNormalBuffer = m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled();
            bool clearColorBuffer = NeedClearColorBuffer(hdCamera);

            RenderGraphMutableResource depthBuffer = m_RenderGraph.CreateTexture(new TextureDesc(Vector2.one) { depthBufferBits = DepthBits.Depth32, clearBuffer = true,  bindTextureMS = msaa, enableMSAA = msaa, xrInstancing = true, useDynamicScale = true, name = "CameraDepthStencil" });
            RenderGraphMutableResource normalBuffer = m_RenderGraph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = clearNormalBuffer, clearColor = Color.black, bindTextureMS = msaa, enableMSAA = msaa, xrInstancing = true, useDynamicScale = true, enableRandomWrite = !msaa, name = "NormalBuffer" });
            RenderGraphMutableResource depthAsColorMSAABuffer = msaa ? m_RenderGraph.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R32_SFloat, clearBuffer = true, clearColor = Color.black, bindTextureMS = true, enableMSAA = true, xrInstancing = true, useDynamicScale = true, name = "DepthAsColorMSAA" }) : new RenderGraphMutableResource();

            StartStereoRendering(cmd, renderContext, camera);
            RenderDepthPrepass(m_RenderGraph, cullingResults, hdCamera, depthBuffer, normalBuffer, depthAsColorMSAABuffer);

            RenderGraph.RenderGraphGlobalParams renderGraphParams = new RenderGraph.RenderGraphGlobalParams();
            renderGraphParams.renderingViewport = hdCamera.renderingViewport;

            m_RenderGraph.Execute(renderContext, cmd, renderGraphParams);
        }

        class PrepassData : RenderGraph.RenderPassData
        {
            public HDCamera hdCamera;
            public Rect renderingViewport;
            public CullingResults cullResult;
            public bool excludeMotion;
            public bool msaaEnabled;
            public ShaderTagId[] firstPassNames;
            public RenderQueueRange firstPassRenderQueue;
            public ShaderTagId[] secondPassNames;
            public RenderQueueRange secondPassRenderQueue;

            public RenderGraphMutableResource depthBuffer;
            public RenderGraphMutableResource depthAsColorBuffer;
            public RenderGraphMutableResource normalBuffer;
        }

        // RenderDepthPrepass render both opaque and opaque alpha tested based on engine configuration.
        // Lit Forward only: We always render all materials
        // Lit Deferred: We always render depth prepass for alpha tested (optimization), other deferred material are render based on engine configuration.
        // Forward opaque with deferred renderer (DepthForwardOnly pass): We always render all materials
        // True is return if motion vector must be render after GBuffer pass
        bool RenderDepthPrepass(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera, RenderGraphMutableResource depthBuffer, RenderGraphMutableResource normalBuffer, RenderGraphMutableResource depthAsColorMSAA)
        {
            // Guidelines:
            // Lit shader can be in deferred or forward mode. In this case we use "DepthOnly" pass with "GBuffer" or "Forward" pass name
            // Other shader, including unlit are always forward and use "DepthForwardOnly" with "ForwardOnly" pass.
            // Those pass are exclusive so use only "DepthOnly" or "DepthForwardOnly" but not both at the same time, same for "Forward" and "DepthForwardOnly"
            // Any opaque material rendered in forward should have a depth prepass. If there is no depth prepass the lighting will be incorrect (deferred shadowing, contact shadow, SSAO), this may be acceptable depends on usage

            // Whatever the configuration we always render first opaque object then opaque alpha tested as they are more costly to render and could be reject by early-z
            // (but no Hi-z as it is disable with clip instruction). This is handled automatically with the RenderQueue value (OpaqueAlphaTested have a different value and thus are sorted after Opaque)

            // Forward material always output normal buffer.
            // Deferred material never output normal buffer.
            // Caution: Unlit material let normal buffer untouch. Caution as if people try to filter normal buffer, it can result in weird result.
            // TODO: Do we need a stencil bit to identify normal buffer not fill by unlit? So don't execute SSAO / SRR ?

            // Additional guidelines for motion vector:
            // We render object motion vector at the same time than depth prepass with MRT to save drawcall. Depth buffer is then fill with combination of depth prepass + motion vector.
            // For this we render first all objects that render depth only, then object that require object motion vector.
            // We use the excludeMotion filter option of DrawRenderer to gather object without object motion vector (only C++ can know if an object have object motion vector).
            // Caution: if there is no depth prepass we must render object motion vector after GBuffer pass otherwise some depth only objects can hide objects with motion vector and overwrite depth buffer but not update
            // the motion vector buffer resulting in artifacts

            bool fullDeferredPrepass = hdCamera.frameSettings.IsEnabled(FrameSettingsField.DepthPrepassWithDeferredRendering) || m_DbufferManager.enableDecals;
            // To avoid rendering objects twice (once in the depth pre-pass and once in the motion vector pass when the motion vector pass is enabled) we exclude the objects that have motion vectors.
            bool objectMotionEnabled = hdCamera.frameSettings.IsEnabled(FrameSettingsField.ObjectMotionVectors);
            bool shouldRenderMotionVectorAfterGBuffer = (hdCamera.frameSettings.litShaderMode == LitShaderMode.Deferred) && !fullDeferredPrepass;
            bool msaa = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);

            switch (hdCamera.frameSettings.litShaderMode)
            {
                case LitShaderMode.Forward:
                    {
                        using (var builder = renderGraph.AddRenderPass<PrepassData>("Depth Prepass (forward)", out var passData, CustomSamplerId.DepthPrepass.GetSampler()))
                        {
                            passData.hdCamera = hdCamera;
                            passData.renderingViewport = hdCamera.renderingViewport;
                            passData.cullResult = cull;
                            passData.excludeMotion = objectMotionEnabled;
                            passData.msaaEnabled = msaa;
                            passData.firstPassNames = m_DepthOnlyAndDepthForwardOnlyPassNames;
                            passData.firstPassRenderQueue = HDRenderQueue.k_RenderQueue_AllOpaque;

                            passData.depthBuffer = builder.WriteTexture(depthBuffer);
                            passData.normalBuffer = builder.WriteTexture(normalBuffer);
                            if (msaa)
                                passData.depthAsColorBuffer = builder.WriteTexture(depthAsColorMSAA);

                            builder.SetRenderFunc(
                            (RenderGraph.RenderPassData data, RenderGraph.RenderGraphGlobalParams globalParams, RenderGraph.RenderGraphContext renderGraphContext) =>
                            {
                                PrepassData prepassData = (PrepassData)data;

                                var mrt = RenderGraphUtils.GetMRTArray(prepassData.msaaEnabled ? 2 : 1);
                                mrt[0] = renderGraphContext.resources.GetTexture(prepassData.normalBuffer);
                                if (prepassData.msaaEnabled)
                                    mrt[1] = renderGraphContext.resources.GetTexture(prepassData.depthAsColorBuffer);

                                HDUtils.SetRenderTarget(renderGraphContext.cmd, prepassData.renderingViewport, mrt, renderGraphContext.resources.GetTexture(prepassData.depthBuffer));
                                // XRTODO: wait for XR SDK integration and implement custom version in HDUtils with dynamic resolution support
                                //XRUtils.DrawOcclusionMesh(cmd, hdCamera.camera, hdCamera.camera.stereoEnabled);

                                // Full forward: Output normal buffer for both forward and forwardOnly
                                // Exclude object that render velocity (if motion vector are enabled)
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderGraphContext.renderContext, renderGraphContext.cmd, prepassData.firstPassNames, 0, prepassData.firstPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);
                            });
                        }
                    }
                    break;
                case LitShaderMode.Deferred:
                    {
                        string passName = fullDeferredPrepass ? (m_DbufferManager.enableDecals ? "Depth Prepass (deferred) forced by Decals" : "Depth Prepass (deferred)") : "Depth Prepass (deferred incomplete)";
                        bool excludeMotion = fullDeferredPrepass ? objectMotionEnabled : false;

                        // First deferred alpha tested materials. Alpha tested object have always a prepass even if enableDepthPrepassWithDeferredRendering is disabled
                        var partialPrepassRenderQueueRange = new RenderQueueRange { lowerBound = (int)RenderQueue.AlphaTest, upperBound = (int)RenderQueue.GeometryLast - 1 };

                        using (var builder = renderGraph.AddRenderPass<PrepassData>(passName, out var passData, CustomSamplerId.DepthPrepass.GetSampler()))
                        {
                            passData.hdCamera = hdCamera;
                            passData.renderingViewport = hdCamera.renderingViewport;
                            passData.cullResult = cull;
                            passData.excludeMotion = excludeMotion;
                            passData.msaaEnabled = msaa;
                            passData.firstPassNames = m_DepthOnlyPassNames;
                            passData.firstPassRenderQueue = fullDeferredPrepass ? HDRenderQueue.k_RenderQueue_AllOpaque : partialPrepassRenderQueueRange;
                            passData.secondPassNames = m_DepthForwardOnlyPassNames;
                            passData.secondPassRenderQueue = HDRenderQueue.k_RenderQueue_AllOpaque;

                            passData.depthBuffer = builder.WriteTexture(depthBuffer);
                            passData.normalBuffer = builder.WriteTexture(normalBuffer);
                            if (msaa)
                                passData.depthAsColorBuffer = builder.WriteTexture(depthAsColorMSAA);

                            builder.SetRenderFunc(
                            (RenderGraph.RenderPassData data, RenderGraph.RenderGraphGlobalParams globalParams, RenderGraph.RenderGraphContext renderGraphContext) =>
                            {
                                PrepassData prepassData = (PrepassData)data;

                                // First deferred material
                                HDUtils.SetRenderTarget(renderGraphContext.cmd, prepassData.renderingViewport, renderGraphContext.resources.GetTexture(prepassData.depthBuffer));
                                // XRTODO: wait for XR SDK integration and implement custom version in HDUtils with dynamic resolution support
                                //XRUtils.DrawOcclusionMesh(cmd, hdCamera.camera, hdCamera.camera.stereoEnabled);
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderGraphContext.renderContext, renderGraphContext.cmd, prepassData.firstPassNames, 0, prepassData.firstPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);

                                // Then forward only material that output normal buffer
                                var mrt = RenderGraphUtils.GetMRTArray(prepassData.msaaEnabled ? 2 : 1);
                                mrt[0] = renderGraphContext.resources.GetTexture(prepassData.normalBuffer);
                                if (prepassData.msaaEnabled)
                                    mrt[1] = renderGraphContext.resources.GetTexture(prepassData.depthAsColorBuffer);

                                HDUtils.SetRenderTarget(renderGraphContext.cmd, prepassData.renderingViewport, mrt, renderGraphContext.resources.GetTexture(prepassData.depthBuffer));
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderGraphContext.renderContext, renderGraphContext.cmd, prepassData.secondPassNames, 0, prepassData.secondPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);
                            });
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unknown ShaderLitMode");
            }
            //});

    #if ENABLE_RAYTRACING
                // If there is a ray-tracing environment and the feature is enabled we want to push these objects to the prepass
                HDRaytracingEnvironment currentEnv = m_RayTracingManager.CurrentEnvironment();
                // We want the opaque objects to be in the prepass so that we avoid rendering uselessly the pixels before raytracing them
                if (currentEnv != null && currentEnv.raytracedObjects)
                    RenderOpaqueRenderList(cull, hdCamera, renderContext, cmd, m_DepthOnlyAndDepthForwardOnlyPassNames, 0, HDRenderQueue.k_RenderQueue_AllOpaqueRaytracing);
    #endif

            return shouldRenderMotionVectorAfterGBuffer;
        }

        bool NeedClearColorBuffer(HDCamera hdCamera)
        {
            if (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Color ||
                // If the luxmeter is enabled, the sky isn't rendered so we clear the background color
                m_DebugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter ||
                // If we want the sky but the sky don't exist, still clear with background color
                (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Sky && !m_SkyManager.IsVisualSkyValid()) ||
                // Special handling for Preview we force to clear with background color (i.e black)
                // Note that the sky use in this case is the last one setup. If there is no scene or game, there is no sky use as reflection in the preview
                HDUtils.IsRegularPreviewCamera(hdCamera.camera)
                )
            {
                return true;
            }

            return false;
        }

        Color GetColorBufferClearColor(HDCamera hdCamera)
        {
            Color clearColor = hdCamera.backgroundColorHDR;
            // We set the background color to black when the luxmeter is enabled to avoid picking the sky color
            if (m_DebugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter)
                clearColor = Color.black;

            return clearColor;
        }
    }
}
