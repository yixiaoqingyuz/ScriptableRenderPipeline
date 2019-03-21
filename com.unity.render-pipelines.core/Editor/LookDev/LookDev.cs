using UnityEngine.Rendering;
using UnityEngine.Rendering.LookDev;

using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor.Rendering.LookDev
{
    /// <summary>
    /// Main entry point for scripting LookDev
    /// </summary>
    public static class LookDev
    {
        const string lastRenderingDataSavePath = "Library/LookDevConfig.asset";

        static LookDevWindow window;
        static LookDevRenderer renderer;
        
        public static bool open { get; private set; }

        static ILookDevDataProvider dataProvider
            => RenderPipelineManager.currentPipeline as ILookDevDataProvider;

        /// <summary>
        /// Does LookDev is supported with the current render pipeline?
        /// </summary>
        public static bool supported => dataProvider != null;
        
        public static LookDevContent sceneContents { get; set; } = new LookDevContent();
        
        public static LookDevContext currentContext { get; set; }

        static LookDev()
            => currentContext = LoadConfigInternal() ?? GetDefaultContext();

        static LookDevContext GetDefaultContext()
            => UnityEngine.ScriptableObject.CreateInstance<LookDevContext>();

        public static void ResetConfig()
            => currentContext = GetDefaultContext();

        static LookDevContext LoadConfigInternal(string path = lastRenderingDataSavePath)
        {
            var last = InternalEditorUtility.LoadSerializedFileAndForget(path)?[0] as LookDevContext;
            if (last != null && !last.Equals(null))
                return ((LookDevContext)last);
            return null;
        }

        public static void LoadConfig(string path = lastRenderingDataSavePath)
        {
            var last = LoadConfigInternal(path);
            if (last != null)
                currentContext = last;
        }

        public static void SaveConfig(string path = lastRenderingDataSavePath)
            => InternalEditorUtility.SaveToSerializedFileAndForget(new[] { currentContext }, path, true);

        [MenuItem("Window/Experimental/NEW Look Dev", false, -1)]
        public static void Open()
        {
            window = EditorWindow.GetWindow<LookDevWindow>();
            window.titleContent = LookDevStyle.WindowTitleAndIcon;
            ConfigureLookDev();
        }

        [Callbacks.DidReloadScripts]
        static void OnEditorReload()
        {
            var windows = Resources.FindObjectsOfTypeAll<LookDevWindow>();
            window = windows.Length > 0 ? windows[0] : null;
            open = window != null;
            if (open)
                ConfigureLookDev();
        }

        static void ConfigureLookDev()
        {
            open = true;
            LoadConfig();
            ConfigureRenderer();
        }

        static void ConfigureRenderer()
        {
            renderer = new LookDevRenderer(window, currentContext, sceneContents);
            window.OnWindowClosed += () =>
            {
                renderer.CleanUp();
                renderer = null;

                SaveConfig();

                open = false;
            };
        }
    }
}
