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

        //TODO: ensure only one displayer at time for the moment
        static DisplayWindow window;
        static Renderer renderer;
        
        public static bool open { get; private set; }

        static IDataProvider dataProvider
            => RenderPipelineManager.currentPipeline as IDataProvider;

        /// <summary>
        /// Does LookDev is supported with the current render pipeline?
        /// </summary>
        public static bool supported => dataProvider != null;
        
        public static Context currentContext { get; private set; }

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
            {
                last.Validate();
                currentContext = last;
            }
        }

        public static void SaveConfig(string path = lastRenderingDataSavePath)
        {
            if (currentContext != null && !currentContext.Equals(null))
                InternalEditorUtility.SaveToSerializedFileAndForget(new[] { currentContext }, path, true);
        }

        [MenuItem("Window/Experimental/NEW Look Dev", false, -1)]
        public static void Open()
        {
            if (!supported)
                throw new System.Exception("LookDev is not supported by this Scriptable Render Pipeline");

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
            WaitingSRPReloadForConfiguringRenderer(5);
        }

        static void WaitingSRPReloadForConfiguringRenderer(int maxAttempt, int attemptNumber = 0)
        {
            if (supported)
                ConfigureRenderer();
            else if (attemptNumber < maxAttempt)
                EditorApplication.delayCall +=
                    () => WaitingSRPReloadForConfiguringRenderer(maxAttempt, ++attemptNumber);
            else
                window.Close();
        }
        
        static void ConfigureRenderer()
        {
            renderer = new Renderer(window, currentContext, dataProvider);
            window.OnWindowClosed += () =>
            {
                renderer.Dispose();
                renderer = null;

                SaveConfig();

                open = false;
            };
        }

        public static void PushSceneChangesToRenderer(ViewIndex index)
            => renderer?.UpdateScene(index);
    }
}
