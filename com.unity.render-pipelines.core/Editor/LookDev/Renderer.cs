using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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

    //TODO: for automatising environment change
    // if Unsupported.SetOverrideRenderSettings
    // do not behave as expected (see Stage)
    struct EnvironmentScope : IDisposable
    {
        public EnvironmentScope(bool b)
        {
        }

        void IDisposable.Dispose()
        {
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
        //TODO: add a second stage
        Stage m_Stage;

        Color m_AmbientColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        bool m_PixelPerfect;

        public Renderer(
            IDisplayer displayer,
            Context contexts)
        {
            this.m_Displayer = displayer;
            this.m_Contexts = contexts;

            //TODO: add a second stage
            m_Stage = new Stage("LookDevViewA");

            EditorApplication.update += Render;
        }

        void CleanUp()
            => EditorApplication.update -= Render;
        public void Dispose()
        {
            CleanUp();
            GC.SuppressFinalize(this);
        }
        ~Renderer() => CleanUp();


        public void UpdateScene()
        {
            m_Stage.Clear();
            var viewContent = m_Contexts.GetViewContent(ViewIndex.FirstOrFull);
            if (viewContent == null)
            {
                viewContent.prefabInstanceInPreview = null;
                return;
            }

            if (viewContent.contentPrefab != null && !viewContent.contentPrefab.Equals(null))
                viewContent.prefabInstanceInPreview = m_Stage.InstantiateInStage(viewContent.contentPrefab);
        }

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

            cameraState.UpdateCamera(m_Stage.camera);
            m_Stage.camera.aspect = previewRect.width / previewRect.height;
            
            m_Stage.camera.Render();

            return EndPreview(index);
        }

        void BeginPreview(Rect rect, ViewCompositionIndex index)
        {
            if (index != ViewCompositionIndex.Composite)
                //TODO: handle multi-stage
                m_Stage.SetGameObjectVisible(true);

            //TODO: handle multi-stage
            m_RenderTextures.UpdateSize(rect, index, m_PixelPerfect, m_Stage.camera);

            //TODO: check scissor
            //TODO: check default (without style) clear
            //m_SavedState = new SavedRenderTargetState();
            //EditorGUIUtility.SetRenderTextureNoViewport(m_RenderTexture);
            //GL.LoadOrtho();
            //GL.LoadPixelMatrix(0, m_RenderTexture.width, m_RenderTexture.height, 0);
            //ShaderUtil.rawViewportRect = new Rect(0, 0, m_RenderTexture.width, m_RenderTexture.height);
            //ShaderUtil.rawScissorRect = new Rect(0, 0, m_RenderTexture.width, m_RenderTexture.height);
            //GL.Clear(true, true, camera.backgroundColor);
        }

        RenderTexture EndPreview(ViewCompositionIndex index)
        {

            if (index != ViewCompositionIndex.Composite)
                //TODO: handle multi-stage
                m_Stage.SetGameObjectVisible(false);

            //m_SavedState.Restore();

            return m_RenderTextures[index];
        }
    }
}
