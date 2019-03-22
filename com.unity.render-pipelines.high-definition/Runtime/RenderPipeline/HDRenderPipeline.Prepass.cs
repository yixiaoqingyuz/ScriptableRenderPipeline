using System;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class HDRenderPipeline
    {
        class DepthPrepassData : RenderPassData
        {
            public FrameSettings frameSettings;
            public Rect renderingViewport;
            public bool msaaEnabled;

            public RenderGraphMutableResource depthBuffer;
            public RenderGraphMutableResource depthAsColorBuffer;
            public RenderGraphMutableResource normalBuffer;

            public RenderGraphResource rendererList1;
            public RenderGraphResource rendererList2;
        }

        // RenderDepthPrepass render both opaque and opaque alpha tested based on engine configuration.
        // Lit Forward only: We always render all materials
        // Lit Deferred: We always render depth prepass for alpha tested (optimization), other deferred material are render based on engine configuration.
        // Forward opaque with deferred renderer (DepthForwardOnly pass): We always render all materials
        // True is returned if motion vector must be rendered after GBuffer pass
        bool RenderDepthPrepass(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera)
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
                    using (var builder = renderGraph.AddRenderPass<DepthPrepassData>("Depth Prepass (forward)", out var passData, CustomSamplerId.DepthPrepass.GetSampler()))
                    {
                        passData.frameSettings = hdCamera.frameSettings;
                        passData.msaaEnabled = msaa;

                        passData.depthBuffer = builder.UseDepthBuffer(m_SharedRTManager.GetDepthStencilBufferResource(msaa));
                        passData.normalBuffer = builder.UseColorBuffer(m_SharedRTManager.GetNormalBufferResource(msaa), 0);
                        if (msaa)
                            passData.depthAsColorBuffer = builder.UseColorBuffer(m_SharedRTManager.GetDepthTextureResource(true), 1);

                        // Full forward: Output normal buffer for both forward and forwardOnly
                        // Exclude object that render velocity (if motion vector are enabled)
                        passData.rendererList1 = builder.UseRendererList(
                            builder.CreateRendererList(new RendererListDesc(m_DepthOnlyAndDepthForwardOnlyPassNames, cull, hdCamera.camera)
                            {
                                renderQueueRange = HDRenderQueue.k_RenderQueue_AllOpaque,
                                sortingCriteria = SortingCriteria.CommonOpaque,
                                excludeMotionVectors = objectMotionEnabled
                            }));

                        builder.SetRenderFunc(
                        (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                        {
                            DepthPrepassData prepassData = (DepthPrepassData)data;
                            DrawOpaqueRendererList(prepassData.frameSettings, renderGraphContext.resources.GetRendererList(prepassData.rendererList1), renderGraphContext.renderContext, renderGraphContext.cmd);
                        });
                    }
                    break;
                case LitShaderMode.Deferred:
                    string passName = fullDeferredPrepass ? (m_DbufferManager.enableDecals ? "Depth Prepass (deferred) forced by Decals" : "Depth Prepass (deferred)") : "Depth Prepass (deferred incomplete)";
                    bool excludeMotion = fullDeferredPrepass ? objectMotionEnabled : false;

                    // First deferred alpha tested materials. Alpha tested object have always a prepass even if enableDepthPrepassWithDeferredRendering is disabled
                    var partialPrepassRenderQueueRange = new RenderQueueRange { lowerBound = (int)RenderQueue.AlphaTest, upperBound = (int)RenderQueue.GeometryLast - 1 };

                    using (var builder = renderGraph.AddRenderPass<DepthPrepassData>(passName, out var passData, CustomSamplerId.DepthPrepass.GetSampler()))
                    {
                        passData.frameSettings = hdCamera.frameSettings;
                        passData.renderingViewport = hdCamera.renderingViewport;
                        passData.msaaEnabled = msaa;

                        // First deferred material
                        passData.rendererList1 = builder.UseRendererList(
                            builder.CreateRendererList(new RendererListDesc(m_DepthOnlyPassNames, cull, hdCamera.camera)
                            {
                                renderQueueRange = fullDeferredPrepass ? HDRenderQueue.k_RenderQueue_AllOpaque : partialPrepassRenderQueueRange,
                                sortingCriteria = SortingCriteria.CommonOpaque,
                                excludeMotionVectors = excludeMotion
                            }));

                        // Then forward only material that output normal buffer
                        passData.rendererList2 = builder.UseRendererList(
                            builder.CreateRendererList(new RendererListDesc(m_DepthForwardOnlyPassNames, cull, hdCamera.camera)
                            {
                                renderQueueRange = HDRenderQueue.k_RenderQueue_AllOpaque,
                                sortingCriteria = SortingCriteria.CommonOpaque,
                                excludeMotionVectors = excludeMotion
                            }));

                        passData.depthBuffer = builder.WriteTexture(m_SharedRTManager.GetDepthStencilBufferResource(msaa));
                        passData.normalBuffer = builder.WriteTexture(m_SharedRTManager.GetNormalBufferResource(msaa));
                        if (msaa)
                            passData.depthAsColorBuffer = builder.WriteTexture(m_SharedRTManager.GetDepthTextureResource(true));

                        builder.SetRenderFunc(
                        (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                        {
                            DepthPrepassData prepassData = (DepthPrepassData)data;

                            HDUtils.SetRenderTarget(renderGraphContext.cmd, prepassData.renderingViewport, renderGraphContext.resources.GetTexture(prepassData.depthBuffer));
                            // XRTODO: wait for XR SDK integration and implement custom version in HDUtils with dynamic resolution support
                            //XRUtils.DrawOcclusionMesh(cmd, hdCamera.camera, hdCamera.camera.stereoEnabled);
                            DrawOpaqueRendererList(prepassData.frameSettings, renderGraphContext.resources.GetRendererList(prepassData.rendererList1), renderGraphContext.renderContext, renderGraphContext.cmd);

                            var mrt = RenderGraphUtils.GetMRTArray(prepassData.msaaEnabled ? 2 : 1);
                            mrt[0] = renderGraphContext.resources.GetTexture(prepassData.normalBuffer);
                            if (prepassData.msaaEnabled)
                                mrt[1] = renderGraphContext.resources.GetTexture(prepassData.depthAsColorBuffer);

                            HDUtils.SetRenderTarget(renderGraphContext.cmd, prepassData.renderingViewport, mrt, renderGraphContext.resources.GetTexture(prepassData.depthBuffer));
                            DrawOpaqueRendererList(prepassData.frameSettings, renderGraphContext.resources.GetRendererList(prepassData.rendererList2), renderGraphContext.renderContext, renderGraphContext.cmd);
                        });
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unknown ShaderLitMode");
            }

    #if ENABLE_RAYTRACING
                // If there is a ray-tracing environment and the feature is enabled we want to push these objects to the prepass
                HDRaytracingEnvironment currentEnv = m_RayTracingManager.CurrentEnvironment();
                // We want the opaque objects to be in the prepass so that we avoid rendering uselessly the pixels before raytracing them
                if (currentEnv != null && currentEnv.raytracedObjects)
                    RenderOpaqueRenderList(cull, hdCamera, renderContext, cmd, m_DepthOnlyAndDepthForwardOnlyPassNames, 0, HDRenderQueue.k_RenderQueue_AllOpaqueRaytracing);
    #endif

            return shouldRenderMotionVectorAfterGBuffer;
        }

        class ObjectVelocityPassData : RenderPassData
        {
            public FrameSettings                frameSettings;
            public RenderGraphMutableResource   depthBuffer;
            public RenderGraphMutableResource   velocityBuffer;
            public RenderGraphMutableResource   normalBuffer;
            public RenderGraphMutableResource   depthAsColorMSAABuffer;
            public RenderGraphResource          rendererList;
        }

        void RenderObjectsVelocityPass(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera)
        {
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.ObjectMotionVectors))
                return;

            using (var builder = renderGraph.AddRenderPass<ObjectVelocityPassData>("Objects Velocity", out var passData, CustomSamplerId.ObjectsVelocity.GetSampler()))
            {
                bool msaa = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);

                passData.frameSettings = hdCamera.frameSettings;
                passData.depthBuffer = builder.UseDepthBuffer(m_SharedRTManager.GetDepthStencilBufferResource(msaa));
                passData.velocityBuffer = builder.UseColorBuffer(m_SharedRTManager.GetVelocityBufferResource(msaa), 0);
                passData.normalBuffer = builder.UseColorBuffer(m_SharedRTManager.GetNormalBufferResource(msaa), 1);
                if (msaa)
                    passData.depthAsColorMSAABuffer = builder.UseColorBuffer(m_SharedRTManager.GetDepthTextureResource(msaa), 2);

                passData.rendererList = builder.UseRendererList(
                    builder.CreateRendererList(new RendererListDesc(HDShaderPassNames.s_MotionVectorsName, cull, hdCamera.camera)
                    {
                        renderQueueRange = HDRenderQueue.k_RenderQueue_AllOpaque,
                        sortingCriteria = SortingCriteria.CommonOpaque,
                        rendererConfiguration = PerObjectData.MotionVectors
                    }));

                builder.SetRenderFunc(
                (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                {
                    ObjectVelocityPassData prepassData = (ObjectVelocityPassData)data;
                    DrawOpaqueRendererList(prepassData.frameSettings, renderGraphContext.resources.GetRendererList(prepassData.rendererList), renderGraphContext.renderContext, renderGraphContext.cmd);
                });
            }
        }

        class ResolvePrepassData : RenderPassData
        {
            public RenderGraphMutableResource   depthBuffer;
            public RenderGraphMutableResource   depthValuesBuffer;
            public RenderGraphMutableResource   normalBuffer;
            public RenderGraphResource          depthAsColorBufferMSAA;
            public RenderGraphResource          normalBufferMSAA;
            public Material                     depthResolveMaterial;
            public int                          depthresolvePassIndex;
        }

        RenderGraphResource ResolvePrepassBuffers(RenderGraph renderGraph, HDCamera hdCamera)
        {
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA))
                return new RenderGraphResource();

            using (var builder = renderGraph.AddRenderPass<ResolvePrepassData>("Resolve Prepass MSAA", out var passData))
            {
                // This texture stores a set of depth values that are required for evaluating a bunch of effects in MSAA mode (R = Samples Max Depth, G = Samples Min Depth, G =  Samples Average Depth)
                RenderGraphMutableResource depthValuesBuffer = builder.CreateTexture(new TextureDesc(Vector2.one) { colorFormat = GraphicsFormat.R32G32B32A32_SFloat, xrInstancing = true, useDynamicScale = true, name = "DepthValuesBuffer" });

                passData.depthResolveMaterial = m_DepthResolveMaterial;
                passData.depthresolvePassIndex = SampleCountToPassIndex(m_MSAASamples);

                passData.depthBuffer = builder.UseDepthBuffer(m_SharedRTManager.GetDepthStencilBufferResource(false));
                //passData.velocityBuffer = builder.UseColorBuffer(m_SharedRTManager.GetVelocityBufferResource(msaa), 0);
                passData.depthValuesBuffer = builder.UseColorBuffer(depthValuesBuffer, 0);
                passData.normalBuffer = builder.UseColorBuffer(m_SharedRTManager.GetNormalBufferResource(false), 1);

                passData.normalBufferMSAA = builder.ReadTexture(m_SharedRTManager.GetNormalBufferResource(true));
                passData.depthAsColorBufferMSAA = builder.ReadTexture(m_SharedRTManager.GetDepthTextureResource(true));

                builder.SetRenderFunc(
                (RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext) =>
                {
                    ResolvePrepassData resolvePrepassData = (ResolvePrepassData)data;
                    renderGraphContext.cmd.DrawProcedural(Matrix4x4.identity, resolvePrepassData.depthResolveMaterial, resolvePrepassData.depthresolvePassIndex, MeshTopology.Triangles, 3, 1);
                });

                return depthValuesBuffer;
            }
        }
    }
}
