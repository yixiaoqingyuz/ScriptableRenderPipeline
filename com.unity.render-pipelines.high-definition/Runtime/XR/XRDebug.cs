using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public enum XRDebugMode
    {
        None,
        Composite,
    }

    public static class XRDebugMenu
    {
        public static XRDebugMode debugMode { get; set; }
        public static bool displayCompositeBorders;
        public static bool animateCompositeTiles;
        
        static GUIContent[] debugModeStrings = null;
        static int[] debugModeValues = null;
        
        public static void Init()
        {
            debugModeValues = (int[])Enum.GetValues(typeof(XRDebugMode));
            debugModeStrings = Enum.GetNames(typeof(XRDebugMode))
                .Select(t => new GUIContent(t))
                .ToArray();
        }

        public static void Reset()
        {
            debugMode = XRDebugMode.None;
            displayCompositeBorders = false;
            animateCompositeTiles = false;
        }

        public static void AddWidgets(List<DebugUI.Widget> widgetList, Action<DebugUI.Field<int>, int> RefreshCallback)
        {
            widgetList.AddRange(new DebugUI.Widget[]
            {
                new DebugUI.EnumField { displayName = "XR Debug Mode", getter = () => (int)debugMode, setter = value => debugMode = (XRDebugMode)value, enumNames = debugModeStrings, enumValues = debugModeValues, getIndex = () => (int)debugMode, setIndex = value => debugMode = (XRDebugMode)value, onValueChanged = RefreshCallback },
            });

            if (debugMode == XRDebugMode.Composite)
            {
                widgetList.Add(new DebugUI.Container
                {
                    children =
                    {
                        new DebugUI.BoolField { displayName = "Display borders", getter = () => displayCompositeBorders, setter = value => displayCompositeBorders = value },
                        new DebugUI.BoolField { displayName = "Animate tiles",   getter = () => animateCompositeTiles, setter = value => animateCompositeTiles = value }
                    }
                });
            }
        }
    }

    public partial class XRSystem
    {
        private GameObject debugVolume;

        void CreateDebugVolume()
        {
            debugVolume = new GameObject("XRDebugVolume");
            debugVolume.hideFlags = HideFlags.HideInHierarchy;

            var volume = debugVolume.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = float.MaxValue;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

            // Setup vignette to make a thin border between around each view
            var vignette = volume.profile.Add<Vignette>();
            vignette.active = false;
            vignette.intensity.Override(0.6f);
            vignette.smoothness.Override(0.2f);
            vignette.roundness.Override(0.1f);
            vignette.color.Override(Color.red);
        }

        bool ProcessDebugMode(bool xrEnabled, Camera camera, ref List<MultipassCamera> multipassCameras)
        {
            if (camera.cameraType != CameraType.Game || xrEnabled || XRDebugMenu.debugMode == XRDebugMode.None)
            {
                if (debugVolume != null)
                {
                    Object.DestroyImmediate(debugVolume);
                    debugVolume = null;
                }
                
                return false;
            }

            if (debugVolume == null)
                CreateDebugVolume();

            Rect fullViewport = camera.pixelRect;
            if (camera.targetTexture != null)
            {
                fullViewport = new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height);
            }

            // Split into 4 tiles covering the original viewport
            int tileCountX = 2;
            int tileCountY = 2;
            float splitRatio = 2.0f;

            if (XRDebugMenu.animateCompositeTiles)
                splitRatio = 2.0f + Mathf.Sin(Time.time);

            // Use frustum planes to split the projection into 4 parts
            var furstumPlanes = camera.projectionMatrix.decomposeProjection;

            for (int tileY = 0; tileY < tileCountY; ++tileY)
            {
                for (int tileX = 0; tileX < tileCountX; ++tileX)
                {
                    var xrPass = XRPass.Create(passList.Count, camera.targetTexture);

                    float spliRatioX1 = Mathf.Pow((tileX + 0.0f) / tileCountX, splitRatio);
                    float spliRatioX2 = Mathf.Pow((tileX + 1.0f) / tileCountX, splitRatio);
                    float spliRatioY1 = Mathf.Pow((tileY + 0.0f) / tileCountY, splitRatio);
                    float spliRatioY2 = Mathf.Pow((tileY + 1.0f) / tileCountY, splitRatio);

                    var splitPlanes = furstumPlanes;
                    splitPlanes.left   = Mathf.Lerp(furstumPlanes.left,   furstumPlanes.right, spliRatioX1);
                    splitPlanes.right  = Mathf.Lerp(furstumPlanes.left,   furstumPlanes.right, spliRatioX2);
                    splitPlanes.bottom = Mathf.Lerp(furstumPlanes.bottom, furstumPlanes.top,   spliRatioY1);
                    splitPlanes.top    = Mathf.Lerp(furstumPlanes.bottom, furstumPlanes.top,   spliRatioY2);

                    float tileOffsetX = spliRatioX1 * fullViewport.width;
                    float tileOffsetY = spliRatioY1 * fullViewport.height;
                    float tileSizeX = spliRatioX2 * fullViewport.width - tileOffsetX;
                    float tileSizeY = spliRatioY2 * fullViewport.height - tileOffsetY;

                    Rect viewport = new Rect(fullViewport.x + tileOffsetX, fullViewport.y + tileOffsetY, tileSizeX, tileSizeY);

                    xrPass.AddView(Matrix4x4.Frustum(splitPlanes), camera.worldToCameraMatrix, viewport);
                    AddPassToFrame(xrPass, camera, ref multipassCameras);
                }
            }

            // Enable vignette effect as a cheap way to visualize the border of composite tiles
            if (debugVolume.GetComponent<Volume>().profile.TryGet<Vignette>(out Vignette vignette))
                vignette.active = XRDebugMenu.displayCompositeBorders;

            return true;
        }
    }
}
