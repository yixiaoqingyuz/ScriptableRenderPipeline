// XRSystem is where information about XR views and passes are read from 2 exclusive sources:
// - XRDisplaySubsystem from the XR SDK
// - or the 'legacy' C++ stereo rendering path and XRSettings

// XRTODO(2019.3) Deprecate legacy code
// XRTODO(2020.1) Remove legacy code
#if UNITY_2019_2_OR_NEWER
    #define USE_XR_SDK
#endif

using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
#if USE_XR_SDK
using UnityEngine.Experimental.XR;
#endif

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public class XRSystem
    {
        readonly XRPass emptyPass = new XRPass();
        readonly List<XRPass> passList = new List<XRPass>();

#if USE_XR_SDK
        readonly List<XRDisplaySubsystem> displayList = new List<XRDisplaySubsystem>();
#endif

        internal XRPass GetPass(int passId)
        {
            return (passId < 0) ? emptyPass : passList[passId];
        }

        internal void SetupFrame(Camera[] cameras, ref List<MultipassCamera> multipassCameras)
        {
            bool xrSdkActive = false;

#if USE_XR_SDK
            // Refresh XR displays
            SubsystemManager.GetInstances(displayList);

            // XRTODO: bind cameras to XR displays (only display 0 is used for now)
            XRDisplaySubsystem xrDisplay = null;
            if (displayList.Count > 0)
            {
                xrDisplay = displayList[0];
                xrDisplay.disableLegacyRenderer = true;
                xrSdkActive = true;
            }
#endif

            // Validate current state
            Debug.Assert(passList.Count == 0, "XRSystem.ReleaseFrame() was not called!");
            Debug.Assert(!(xrSdkActive && XRGraphics.enabled), "The legacy C++ stereo rendering path must be disabled with XR SDK! Go to Project Settings --> Player --> XR Settings");
            
            foreach (var camera in cameras)
            {
                bool xrEnabled = xrSdkActive || (camera.stereoEnabled && XRGraphics.enabled);

                // XRTODO: support render to texture
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null || !xrEnabled)
                {
                    multipassCameras.Add(new MultipassCamera(camera));
                    continue;
                }

#if USE_XR_SDK
                if (xrSdkActive)
                {
                    for (int renderPassIndex = 0; renderPassIndex < xrDisplay.GetRenderPassCount(); ++renderPassIndex)
                    {
                        xrDisplay.GetRenderPass(renderPassIndex, out var renderPass);

                        if (CanUseInstancing(camera, renderPass))
                        {
                            // XRTODO: instanced views support with XR SDK
                        }
                        else
                        {
                            for (int renderParamIndex = 0; renderParamIndex < renderPass.GetRenderParameterCount(); ++renderParamIndex)
                            {
                                renderPass.GetRenderParameter(camera, renderParamIndex, out var renderParam);

                                var xrPass = XRPass.Create(renderPass);
                                xrPass.AddView(renderParam);

                                AddPassToFrame(xrPass, camera, ref multipassCameras);
                            }
                        }
                    }
                }
                else 
#endif
                {
                    if (XRGraphics.stereoRenderingMode == XRGraphics.StereoRenderingMode.MultiPass)
                    {
                        for (int passIndex = 0; passIndex < 2; ++passIndex)
                        {
                            var xrPass = XRPass.Create();
                            xrPass.AddView(camera, (Camera.StereoscopicEye)passIndex);
                            
                            AddPassToFrame(xrPass, camera, ref multipassCameras);
                        }
                    }
                    else
                    {
                        var xrPass = XRPass.Create();

                        for (int viewIndex = 0; viewIndex < 2; ++viewIndex)
                        {
                            xrPass.AddView(camera, (Camera.StereoscopicEye)viewIndex);
                        }

                        AddPassToFrame(xrPass, camera, ref multipassCameras);
                    }
                }
            }
        }

        internal void ReleaseFrame()
        {
            foreach (var xrPass in passList)
                XRPass.Release(xrPass);

            passList.Clear();
        }

        internal void AddPassToFrame(XRPass passInfo, Camera camera, ref List<MultipassCamera> multipassCameras)
        {
            int passIndex = passList.Count;
            passList.Add(passInfo);
            multipassCameras.Add(new MultipassCamera(camera, passIndex));
        }

#if USE_XR_SDK
        internal bool CanUseInstancing(Camera camera, XRDisplaySubsystem.XRRenderPass renderPass)
        {
            // XRTODO: instanced views support with XR SDK
            return false;

            // check viewCount > 1, valid texture array format and valid slice index
            // limit to 2 for now (until code fully fixed)
        }
#endif
    }
}
