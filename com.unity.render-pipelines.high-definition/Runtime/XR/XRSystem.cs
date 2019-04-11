// XRSystem is where information about XR views and passes are read from 2 exclusive sources:
// - XRDisplaySubsystem from the XR SDK
// - or the 'legacy' C++ stereo rendering path and XRSettings (will be removed in 2020.1)

#if UNITY_2019_3_OR_NEWER && ENABLE_VR
//#define USE_XR_SDK
#endif

using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
#if USE_XR_SDK
using UnityEngine.Experimental.XR;
#endif

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class XRSystem
    {
        // Valid empty pass when a camera is not using XR
        public static readonly XRPass emptyPass = new XRPass();

        // Store active passes and avoid allocating memory every frames
        List<XRPass> passList = new List<XRPass>();

#if USE_XR_SDK
        List<XRDisplaySubsystem> displayList = new List<XRDisplaySubsystem>();
        XRDisplaySubsystem display = null;
#endif

        internal void SetupFrame(Camera[] cameras, ref List<MultipassCamera> multipassCameras)
        {
            bool xrSdkActive = RefreshXrSdk();

            Debug.Assert(passList.Count == 0, "XRSystem.ReleaseFrame() was not called!");
            Debug.Assert(!(xrSdkActive && XRGraphics.enabled), "The legacy C++ stereo rendering path must be disabled with XR SDK! Go to Project Settings --> Player --> XR Settings");
            
            foreach (var camera in cameras)
            {
                // Read XR SDK or legacy settings
                bool xrEnabled = xrSdkActive || (camera.stereoEnabled && XRGraphics.enabled);

                // Enable XR layout only for gameview camera
                // XRTODO: support render to texture
                bool xrSupported = camera.cameraType == CameraType.Game && camera.targetTexture == null;

                // Debug modes can override the entire layout
                if (ProcessDebugMode(xrEnabled, camera, ref multipassCameras))
                    continue;

                if (xrEnabled && xrSupported)
                {
                    if (xrSdkActive)
                    {
                        CreateLayoutFromXrSdk(camera, ref multipassCameras);
                    }
                    else
                    {
                        CreateLayoutLegacyStereo(camera, ref multipassCameras);
                    }
                }
                else
                {
                    multipassCameras.Add(new MultipassCamera(camera));
                }
            }
        }

        bool RefreshXrSdk()
        {
#if USE_XR_SDK
            SubsystemManager.GetInstances(displayList);

            // XRTODO: bind cameras to XR displays (only display 0 is used for now)
            if (displayList.Count > 0)
            {
                display = displayList[0];
                display.disableLegacyRenderer = true;
                return true;
            }
            else
            {
                display = null;
            }
#endif

            return false;
        }

        void CreateLayoutLegacyStereo(Camera camera, ref List<MultipassCamera> multipassCameras)
        {
            if (XRGraphics.stereoRenderingMode == XRGraphics.StereoRenderingMode.MultiPass)
            {
                for (int passIndex = 0; passIndex < 2; ++passIndex)
                {
                    var xrPass = XRPass.Create(passIndex);
                    xrPass.AddView(camera, (Camera.StereoscopicEye)passIndex);

                    AddPassToFrame(xrPass, camera, ref multipassCameras);
                }
            }
            else
            {
                var xrPass = XRPass.Create(passId: 0);

                for (int viewIndex = 0; viewIndex < 2; ++viewIndex)
                {
                    xrPass.AddView(camera, (Camera.StereoscopicEye)viewIndex);
                }

                AddPassToFrame(xrPass, camera, ref multipassCameras);
            }
        }

        void CreateLayoutFromXrSdk(Camera camera, ref List<MultipassCamera> multipassCameras)
        {
#if USE_XR_SDK
            for (int renderPassIndex = 0; renderPassIndex < display.GetRenderPassCount(); ++renderPassIndex)
            {
                display.GetRenderPass(renderPassIndex, out var renderPass);

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
#endif
        }

        internal bool GetCullingParameters(Camera camera, XRPass xrPass, out ScriptableCullingParameters cullingParams)
        {
#if USE_XR_SDK
            if (display != null)
            {
                display.GetCullingParameters(camera, xrPass.cullingPassId, out cullingParams);
            }
            else
#endif
            {
                if (!camera.TryGetCullingParameters(camera.stereoEnabled, out cullingParams))
                    return false;
            }

            return true;
        }

        internal void ClearAll()
        {
            passList = null;

#if USE_XR_SDK
            displayList = null;
            display = null;
#endif
        }

        internal void ReleaseFrame()
        {
            foreach (var xrPass in passList)
                XRPass.Release(xrPass);

            passList.Clear();
        }

        internal void AddPassToFrame(XRPass pass, Camera camera, ref List<MultipassCamera> multipassCameras)
        {
            passList.Add(pass);
            multipassCameras.Add(new MultipassCamera(camera, pass));
        }

#if USE_XR_SDK
        bool CanUseInstancing(Camera camera, XRDisplaySubsystem.XRRenderPass renderPass)
        {
            // XRTODO: instanced views support with XR SDK
            return false;

            // check viewCount > 1, valid texture array format and valid slice index
            // limit to 2 for now (until code fully fixed)
        }
#endif
    }
}
