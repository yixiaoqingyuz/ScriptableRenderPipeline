using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.LookDev
{
    public enum ViewIndex
    {
        FirstOrFull,
        Second
    };

    enum ViewCompositionIndex
    {
        First = ViewIndex.FirstOrFull,
        Second = ViewIndex.Second,
        Composite
    };

    class LookDevRenderTextureCache
    {
        RenderTexture[] m_RTs = new RenderTexture[3];

        public RenderTexture this[ViewCompositionIndex index]
            => m_RTs[(int)index];
        
        public void UpdateSize(Rect rect, ViewCompositionIndex index)
        {
            int width = (int)rect.width;
            int height = (int)rect.height;
            if (m_RTs[(int)index] == null
                || width != m_RTs[(int)index].width
                || height != m_RTs[(int)index].height)
                m_RTs[(int)index] = new RenderTexture(
                    width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        }
    }

    public class SceneContent
    {
        //TODO: list?
        public GameObject contentObject { get; set; }

        //TODO: lights
    }

    public class LookDevContent
    {
        SceneContent[] m_SCs = new SceneContent[2]
        {
            new SceneContent(),
            new SceneContent()
        };

        public SceneContent this[ViewIndex index]
            => m_SCs[(int)index];
    }

    /// <summary>
    /// Rendering logic
    /// </summary>
    internal class LookDevRenderer
    {
        ILookDevDisplayer displayer;
        LookDevContext context;
        LookDevContent content;
        LookDevRenderTextureCache m_RenderTextures = new LookDevRenderTextureCache();
        PreviewRenderUtility previewUtility;

        public LookDevRenderer(
            ILookDevDisplayer displayer,
            LookDevContext context,
            LookDevContent content)
        {
            this.displayer = displayer;
            this.context = context;
            this.content = content;

            previewUtility = new PreviewRenderUtility();

            EditorApplication.update += Render;
        }

        bool cleaned = false;
        internal void CleanUp()
        {
            if (!cleaned)
            {
                cleaned = true;
                EditorApplication.update -= Render;
                previewUtility.Cleanup();
            }
        }
        ~LookDevRenderer() => CleanUp();

        public void Render()
        {
            //if (Event.current.type == EventType.Repaint)
            //{
            //    if (m_LookDevConfig.rotateObjectMode)
            //        m_ObjRotationAcc = Math.Min(m_ObjRotationAcc + Time.deltaTime * 0.5f, 1.0f);
            //    else
            //        // Do brutal stop because weoften want to stop at a particular position
            //        m_ObjRotationAcc = 0.0f; // Math.Max(m_ObjRotationAcc - Time.deltaTime * 0.5f, 0.0f);

            //    if (m_LookDevConfig.rotateEnvMode)
            //        m_EnvRotationAcc = Math.Min(m_EnvRotationAcc + Time.deltaTime * 0.5f, 1.0f);
            //    else
            //        // Do brutal stop because weoften want to stop at a particular position
            //        m_EnvRotationAcc = 0.0f; // Math.Max(m_EnvRotationAcc - Time.deltaTime * 0.5f, 0.0f);

            //    // Handle objects/env rotation
            //    // speed control (in degree) - Time.deltaTime is in seconds
            //    m_CurrentObjRotationOffset = (m_CurrentObjRotationOffset + Time.deltaTime * 360.0f * 0.3f * m_LookDevConfig.objRotationSpeed * m_ObjRotationAcc) % 360.0f;
            //    m_LookDevConfig.lookDevContexts[0].envRotation = (m_LookDevConfig.lookDevContexts[0].envRotation + Time.deltaTime * 360.0f * 0.03f * m_LookDevConfig.envRotationSpeed * m_EnvRotationAcc) % 720.0f; // 720 to match GUI
            //    m_LookDevConfig.lookDevContexts[1].envRotation = (m_LookDevConfig.lookDevContexts[1].envRotation + Time.deltaTime * 360.0f * 0.03f * m_LookDevConfig.envRotationSpeed * m_EnvRotationAcc) % 720.0f; // 720 to match GUI

                switch (context.layout.viewLayout)
                {
                    case LayoutContext.Layout.FullA:
                    RenderSingle(ViewCompositionIndex.First);
                    break;
                case LayoutContext.Layout.FullB:
                    RenderSingle(ViewCompositionIndex.Second);
                    break;
                    case LayoutContext.Layout.HorizontalSplit:
                    case LayoutContext.Layout.VerticalSplit:
                        RenderSideBySide();
                        break;
                    case LayoutContext.Layout.CustomSplit:
                    case LayoutContext.Layout.CustomCircular:
                        RenderDualView();
                        break;
                }
            //}
        }

        bool IsNullArea(Rect r)
            => r.width == 0 || r.height == 0
            || float.IsNaN(r.width) || float.IsNaN(r.height);

        void RenderSingle(ViewCompositionIndex index)
        {
            Rect rect = displayer.GetRect((ViewIndex)index);
            if (IsNullArea(rect))
                return;

            m_RenderTextures.UpdateSize(rect, index);

            var texture = RenderScene(
                rect,
                //context,
                content[(ViewIndex)index].contentObject);

            displayer.SetTexture((ViewIndex)index, texture);
        }

        void RenderSideBySide()
        {

        }

        void RenderDualView()
        {

        }

        private Texture RenderScene(Rect previewRect, GameObject currentObject)
        {
            previewUtility.BeginPreview(previewRect, "IN BigTitle inner");
            
            previewUtility.camera.renderingPath = RenderingPath.DeferredShading;
            previewUtility.camera.backgroundColor = Color.black;
            previewUtility.camera.allowHDR = true;

            //for (int lightIndex = 0; lightIndex < 2; lightIndex++)
            //{
            //    previewUtility.lights[lightIndex].enabled = false;
            //    previewUtility.lights[lightIndex].intensity = 0.0f;
            //    previewUtility.lights[lightIndex].shadows = LightShadows.None;
            //}
            
            //previewUtility.ambientColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

            //RenderSettings.ambientIntensity = 1.0f; // fix this to 1, this parameter should not exist!
            //RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox; // Force skybox for our HDRI
            //RenderSettings.reflectionIntensity = 1.0f;
            
            if (currentObject != null)
            {
                foreach (Renderer renderer in currentObject.GetComponentsInChildren<Renderer>())
                    renderer.enabled = true;
            }

            previewUtility.Render(true, false);

            if (currentObject != null)
            {
                foreach (Renderer renderer in currentObject.GetComponentsInChildren<Renderer>())
                    renderer.enabled = false;
            }

            return previewUtility.EndPreview();
        }

    }
}
