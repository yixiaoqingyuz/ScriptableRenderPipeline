using System;
using UnityEngine;
using UnityEngine.Rendering.LookDev;
using IDataProvider = UnityEngine.Rendering.LookDev.IDataProvider;

namespace UnityEditor.Rendering.LookDev
{
    public class RenderingData
    {
        public bool resized;
        public Stage stage;
        public Rect viewPort;
        public RenderTexture output;
    }
    
    public class Renderer
    {
        public bool pixelPerfect { get; set; }

        public Renderer(bool pixelPerfect = false)
            => this.pixelPerfect = pixelPerfect;

        public void Acquire(RenderingData data)
        {
            if (data.viewPort.IsNullOrInverted())
            {
                data.output = null;
                data.resized = true;
                return;
            }
            
            BeginRendering(data);
            data.stage.camera.Render();
            EndRendering(data);
        }

        void BeginRendering(RenderingData data)
        {
            data.stage.SetGameObjectVisible(true);
            var oldOutput = data.output;
            data.output = RenderTextureCache.UpdateSize(
                data.output, data.viewPort, pixelPerfect, data.stage.camera);
            data.stage.camera.enabled = true;
            data.resized = oldOutput != data.output;
        }

        void EndRendering(RenderingData data)
        {
            data.stage.camera.enabled = false;
            data.stage.SetGameObjectVisible(false);
        }
    }

    public static partial class RectExtension
    {
        public static bool IsNullOrInverted(this Rect r)
            => r.width <= 0f || r.height <= 0f
            || float.IsNaN(r.width) || float.IsNaN(r.height);
    }
}
