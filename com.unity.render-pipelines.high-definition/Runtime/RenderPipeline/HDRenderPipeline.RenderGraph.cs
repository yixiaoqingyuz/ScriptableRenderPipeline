using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class HDRenderPipeline
    {

        //bool msaa = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);
        //RenderGraphMutableResource depthBuffer = m_RenderGraph.ImportTexture(m_SharedRTManager.GetDepthStencilBuffer(msaa));
        //RenderGraphMutableResource normalBuffer = m_RenderGraph.ImportTexture(m_SharedRTManager.GetNormalBuffer(msaa));
        //RenderGraphMutableResource depthAsColorMSAABuffer = msaa ? m_RenderGraph.ImportTexture(m_SharedRTManager.GetDepthTexture(true)) : new RenderGraphMutableResource();


        void ExecuteWithRenderGraph()
        {

        }

        class PrepassData : RenderGraph.RenderPassData
        {
            public HDCamera hdCamera;
            public CullingResults cullResult;
            public bool excludeMotion;
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
                            passData.cullResult = cull;
                            passData.excludeMotion = objectMotionEnabled;
                            passData.firstPassNames = m_DepthOnlyAndDepthForwardOnlyPassNames;
                            passData.firstPassRenderQueue = HDRenderQueue.k_RenderQueue_AllOpaque;

                            passData.depthBuffer = builder.WriteTexture(depthBuffer);
                            passData.normalBuffer = builder.WriteTexture(normalBuffer);
                            if (msaa)
                                passData.depthAsColorBuffer = builder.WriteTexture(depthAsColorMSAA);

                            builder.SetRenderFunc(
                            (RenderGraph.RenderPassData data, RenderGraphResourceRegistry resources, RenderGraphTempPool tempPool, CommandBuffer cmd, ScriptableRenderContext renderContext) =>
                            {
                                PrepassData prepassData = (PrepassData)data;
                                bool msaaEnabled = prepassData.hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);

                                var mrt = RenderGraphUtils.GetMRTArray(msaaEnabled ? 2 : 1);
                                mrt[0] = resources.GetTexture(prepassData.normalBuffer);
                                if (msaaEnabled)
                                    mrt[1] = resources.GetTexture(prepassData.depthAsColorBuffer);

                                HDUtils.SetRenderTarget(cmd, prepassData.hdCamera, mrt, resources.GetTexture(prepassData.depthBuffer));
                                XRUtils.DrawOcclusionMesh(cmd, prepassData.hdCamera.camera, prepassData.hdCamera.camera.stereoEnabled);

                                // Full forward: Output normal buffer for both forward and forwardOnly
                                // Exclude object that render velocity (if motion vector are enabled)
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderContext, cmd, prepassData.firstPassNames, 0, prepassData.firstPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);
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
                            passData.cullResult = cull;
                            passData.excludeMotion = excludeMotion;
                            passData.firstPassNames = m_DepthOnlyPassNames;
                            passData.firstPassRenderQueue = fullDeferredPrepass ? HDRenderQueue.k_RenderQueue_AllOpaque : partialPrepassRenderQueueRange;
                            passData.secondPassNames = m_DepthForwardOnlyPassNames;
                            passData.secondPassRenderQueue = HDRenderQueue.k_RenderQueue_AllOpaque;

                            passData.depthBuffer = builder.WriteTexture(depthBuffer);
                            passData.normalBuffer = builder.WriteTexture(normalBuffer);
                            if (msaa)
                                passData.depthAsColorBuffer = builder.WriteTexture(depthAsColorMSAA);

                            builder.SetRenderFunc(
                            (RenderGraph.RenderPassData data, RenderGraphResourceRegistry resources, RenderGraphTempPool tempPool, CommandBuffer cmd, ScriptableRenderContext renderContext) =>
                            {
                                PrepassData prepassData = (PrepassData)data;
                                bool msaaEnabled = prepassData.hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);

                                // First deferred material
                                HDUtils.SetRenderTarget(cmd, prepassData.hdCamera, resources.GetTexture(prepassData.depthBuffer));
                                XRUtils.DrawOcclusionMesh(cmd, prepassData.hdCamera.camera, prepassData.hdCamera.camera.stereoEnabled);
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderContext, cmd, prepassData.firstPassNames, 0, prepassData.firstPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);

                                // Then forward only material that output normal buffer
                                var mrt = RenderGraphUtils.GetMRTArray(msaa ? 2 : 1);
                                mrt[0] = resources.GetTexture(prepassData.normalBuffer);
                                if (msaa)
                                    mrt[1] = resources.GetTexture(prepassData.depthAsColorBuffer);

                                HDUtils.SetRenderTarget(cmd, prepassData.hdCamera, mrt, resources.GetTexture(prepassData.depthBuffer));
                                RenderOpaqueRenderList(prepassData.cullResult, prepassData.hdCamera, renderContext, cmd, prepassData.secondPassNames, 0, prepassData.secondPassRenderQueue, excludeMotionVector: prepassData.excludeMotion);
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
    }
}
