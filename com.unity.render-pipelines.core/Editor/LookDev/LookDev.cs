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

        static DisplayWindow window;
        static Renderer renderer;
        
        public static bool open { get; private set; }

        static IDataProvider dataProvider
            => RenderPipelineManager.currentPipeline as IDataProvider;

        /// <summary>
        /// Does LookDev is supported with the current render pipeline?
        /// </summary>
        public static bool supported => dataProvider != null;
        
        public static Context currentContext { get; set; }

        public static IDisplayer currentDisplayer => window;

        static LookDev()
            => currentContext = LoadConfigInternal() ?? GetDefaultContext();

        static Context GetDefaultContext()
            => UnityEngine.ScriptableObject.CreateInstance<Context>();

        public static void ResetConfig()
            => currentContext = GetDefaultContext();

        static Context LoadConfigInternal(string path = lastRenderingDataSavePath)
        {
            var objs = InternalEditorUtility.LoadSerializedFileAndForget(path);
            var last = (objs.Length > 0 ? objs[0] : null) as Context;
            if (last != null && !last.Equals(null))
                return ((Context)last);
            return null;
        }

        public static void LoadConfig(string path = lastRenderingDataSavePath)
        {
            var last = LoadConfigInternal(path);
            if (last != null)
                currentContext = last;
        }

        public static void SaveConfig(string path = lastRenderingDataSavePath)
            => InternalEditorUtility.SaveToSerializedFileAndForget(new[] { currentContext ?? new Context() }, path, true);

        [MenuItem("Window/Experimental/NEW Look Dev", false, -1)]
        public static void Open()
        {
            window = EditorWindow.GetWindow<DisplayWindow>();
            ConfigureLookDev();
        }

        [Callbacks.DidReloadScripts]
        static void OnEditorReload()
        {
            var windows = Resources.FindObjectsOfTypeAll<DisplayWindow>();
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
            renderer = new Renderer(window, currentContext);
            window.OnWindowClosed += () =>
            {
                renderer.Dispose();
                renderer = null;

                SaveConfig();

                open = false;
            };
        }

        public static void PushSceneChangesToRenderer() => renderer?.UpdateScene();
    }
}
