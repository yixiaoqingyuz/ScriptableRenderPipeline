//#define TEMPORARY_RENDERDOC_INTEGRATION //require specific c++

using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LookDev;
using UnityEngine.SceneManagement;
using IDataProvider = UnityEngine.Rendering.LookDev.IDataProvider;

namespace UnityEditor.Rendering.LookDev
{
    enum ViewCompositionIndex
    {
        First = ViewIndex.FirstOrFull,
        Second = ViewIndex.Second,
        Composite
    };

    class RenderTextureCache
    {
        RenderTexture[] m_RTs = new RenderTexture[3];

        public RenderTexture this[ViewCompositionIndex index]
            => m_RTs[(int)index];

        public void UpdateSize(Rect rect, ViewCompositionIndex index, bool pixelPerfect, Camera renderingCamera)
        {
            float scaleFactor = GetScaleFactor(rect.width, rect.height, pixelPerfect);
            int width = (int)(rect.width * scaleFactor);
            int height = (int)(rect.height * scaleFactor);
            if (m_RTs[(int)index] == null
                || width != m_RTs[(int)index].width
                || height != m_RTs[(int)index].height)
            {
                if (m_RTs[(int)index] != null)
                    UnityEngine.Object.DestroyImmediate(m_RTs[(int)index]);
                
                // Do not use GetTemporary to manage render textures. Temporary RTs are only
                // garbage collected each N frames, and in the editor we might be wildly resizing
                // the inspector, thus using up tons of memory.
                //GraphicsFormat format = camera.allowHDR ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                //m_RenderTexture = new RenderTexture(rtWidth, rtHeight, 16, format);
                //m_RenderTexture.hideFlags = HideFlags.HideAndDontSave;
                //TODO: check format
                m_RTs[(int)index] = new RenderTexture(
                    width, height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
                m_RTs[(int)index].hideFlags = HideFlags.HideAndDontSave;
                m_RTs[(int)index].name = "LookDevTexture";
                m_RTs[(int)index].Create();

                renderingCamera.targetTexture = m_RTs[(int)index];
            }
        }
        
        float GetScaleFactor(float width, float height, bool pixelPerfect)
        {
            float scaleFacX = Mathf.Max(Mathf.Min(width * 2, 1024), width) / width;
            float scaleFacY = Mathf.Max(Mathf.Min(height * 2, 1024), height) / height;
            float result = Mathf.Min(scaleFacX, scaleFacY) * EditorGUIUtility.pixelsPerPoint;
            if (pixelPerfect)
                result = Mathf.Max(Mathf.Round(result), 1f);
            return result;
        }
    }


    class StageCache
    {
        Stage[] m_Stages;
        Context m_Contexts;

        public Stage this[ViewIndex index]
            => m_Stages[(int)index];

        public bool initialized { get; private set; }

        public StageCache(IDataProvider dataProvider, Context contexts)
        {
            m_Contexts = contexts;
            m_Stages = new Stage[2]
            {
                InitStage("LookDevViewA", dataProvider),
                InitStage("LookDevViewB", dataProvider)
            };
            initialized = true;
        }


        Stage InitStage(string sceneName, IDataProvider dataProvider)
        {
            Stage stage = new Stage(sceneName);

            CustomRenderSettings renderSettings = dataProvider.GetEnvironmentSetup();
            if (Unsupported.SetOverrideRenderSettings(stage.scene))
            {
                RenderSettings.defaultReflectionMode = renderSettings.defaultReflectionMode;
                RenderSettings.customReflection = renderSettings.customReflection;
                RenderSettings.skybox = renderSettings.skybox;
                RenderSettings.ambientMode = renderSettings.ambientMode;
                Unsupported.useScriptableRenderPipeline = true;
                Unsupported.RestoreOverrideRenderSettings();
            }
            else
                throw new System.Exception("Stage's scene was not created correctly");

            dataProvider.SetupCamera(stage.camera);

            return stage;
        }

        public void UpdateScene(ViewIndex index)
        {
            Stage stage = this[index];
            stage.Clear();
            var viewContent = m_Contexts.GetViewContent(index);
            if (viewContent == null)
            {
                viewContent.prefabInstanceInPreview = null;
                return;
            }

            if (viewContent.contentPrefab != null && !viewContent.contentPrefab.Equals(null))
                viewContent.prefabInstanceInPreview = stage.InstantiateIntoStage(viewContent.contentPrefab);
        }
    }


    /// <summary>
    /// Rendering logic
    /// TODO: extract SceneLogic elsewhere
    /// </summary>
    internal class Renderer : IDisposable
    {
        IDisplayer m_Displayer;
        Context m_Contexts;
        RenderTextureCache m_RenderTextures = new RenderTextureCache();

        StageCache m_Stages;

        Color m_AmbientColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        bool m_PixelPerfect;
        bool m_RenderDocAcquisitionRequested;

        public Renderer(
            IDisplayer displayer,
            Context contexts,
            IDataProvider dataProvider)
        {
            m_Displayer = displayer;
            m_Contexts = contexts;
            
            m_Stages = new StageCache(dataProvider, m_Contexts);

            m_Displayer.OnRenderDocAcquisitionTriggered += RenderDocAcquisitionRequested;
            EditorApplication.update += Render;
        }

        void RenderDocAcquisitionRequested()
            => m_RenderDocAcquisitionRequested = true;

        void CleanUp()
        {
            m_Displayer.OnRenderDocAcquisitionTriggered -= RenderDocAcquisitionRequested;
            EditorApplication.update -= Render;
        }
        public void Dispose()
        {
            CleanUp();
            GC.SuppressFinalize(this);
        }
        ~Renderer() => CleanUp();


        public void UpdateScene(ViewIndex index)
            => m_Stages.UpdateScene(index);

        public void Render()
        {
            switch (m_Contexts.layout.viewLayout)
            {
                case Layout.FullA:
                    RenderSingle(ViewIndex.FirstOrFull);
                    break;
                case Layout.FullB:
                    RenderSingle(ViewIndex.Second);
                    break;
                case Layout.HorizontalSplit:
                case Layout.VerticalSplit:
                    RenderSideBySide();
                    break;
                case Layout.CustomSplit:
                case Layout.CustomCircular:
                    RenderDualView();
                    break;
            }

            //stating that RenderDoc do not need to acquire anymore should
            //allows to gather both view and composition in render doc at once
            //TODO: check this
            m_RenderDocAcquisitionRequested = false;
        }

        bool IsNullArea(Rect r)
            => r.width == 0 || r.height == 0
            || float.IsNaN(r.width) || float.IsNaN(r.height);

        void RenderSingle(ViewIndex index)
        {
            Rect rect = m_Displayer.GetRect(index);
            if (IsNullArea(rect))
                return;

            var cameraState = m_Contexts.GetCameraState(index);
            var viewContext = m_Contexts.GetViewContent(index);
            
            var texture = RenderScene(
                rect,
                cameraState,
                (ViewCompositionIndex)index);

            //Texture2D myTexture2D = new Texture2D(texture.width, texture.height);
            //RenderTexture.active = texture;
            //myTexture2D.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            //myTexture2D.Apply();
            //var bytes = myTexture2D.EncodeToPNG();
            //System.IO.File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", bytes);
           
            //Graphics.SetRenderTarget(texture);
            //GL.Clear(false, true, Color.cyan);
            m_Displayer.SetTexture(ViewIndex.FirstOrFull, texture);
            //m_Displayer.SetTexture(ViewIndex.FirstOrFull, Texture2D.whiteTexture);
            //m_Displayer.SetTexture(ViewIndex.FirstOrFull, myTexture2D);
        }

        void RenderSideBySide()
        {

        }

        void RenderDualView()
        {

        }

        RenderTexture RenderScene(Rect previewRect, CameraState cameraState, ViewCompositionIndex index)
        {
            BeginPreview(previewRect, index);

            Camera camera = m_Stages[(ViewIndex)index].camera;
            cameraState.UpdateCamera(camera);
            camera.aspect = previewRect.width / previewRect.height;
            
            camera.Render();

            return EndPreview(index);
        }

        void BeginPreview(Rect rect, ViewCompositionIndex index)
        {
            if (index != ViewCompositionIndex.Composite)
                m_Stages[(ViewIndex)index].SetGameObjectVisible(true);

            Camera camera = m_Stages[(ViewIndex)index].camera;
            m_RenderTextures.UpdateSize(rect, index, m_PixelPerfect, camera);

            //TODO: check scissor
            //TODO: check default (without style) clear
            //m_SavedState = new SavedRenderTargetState();
            //EditorGUIUtility.SetRenderTextureNoViewport(m_RenderTexture);
            //GL.LoadOrtho();
            //GL.LoadPixelMatrix(0, m_RenderTexture.width, m_RenderTexture.height, 0);
            //ShaderUtil.rawViewportRect = new Rect(0, 0, m_RenderTexture.width, m_RenderTexture.height);
            //ShaderUtil.rawScissorRect = new Rect(0, 0, m_RenderTexture.width, m_RenderTexture.height);
            //GL.Clear(true, true, camera.backgroundColor);
            
            camera.enabled = true;

#if TEMPORARY_RENDERDOC_INTEGRATION
            //TODO: make integration EditorWindow agnostic!
            if (RenderDoc.IsLoaded() && RenderDoc.IsSupported() && m_RenderDocAcquisitionRequested)
                RenderDoc.BeginCaptureRenderDoc(m_Displayer as EditorWindow);
#endif
        }

        RenderTexture EndPreview(ViewCompositionIndex index)
        {
#if TEMPORARY_RENDERDOC_INTEGRATION
            //TODO: make integration EditorWindow agnostic!
            if (RenderDoc.IsLoaded() && RenderDoc.IsSupported() && m_RenderDocAcquisitionRequested)
                RenderDoc.EndCaptureRenderDoc(m_Displayer as EditorWindow);
#endif
            Stage stage = m_Stages[(ViewIndex)index];
            stage.camera.enabled = false;

            if (index != ViewCompositionIndex.Composite)
                stage.SetGameObjectVisible(false);

            //m_SavedState.Restore();

            return m_RenderTextures[index];
        }
    }
}
