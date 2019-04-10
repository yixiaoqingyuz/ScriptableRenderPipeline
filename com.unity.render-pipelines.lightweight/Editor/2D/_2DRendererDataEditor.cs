using UnityEditor.Rendering.LWRP;
using UnityEngine.Experimental.Rendering.LWRP;

namespace UnityEditor.Experimental.Rendering.LWRP
{
    [CustomEditor(typeof(_2DRendererData), true)]
    public class _2DRendererDataEditor : ScriptableRendererDataEditor
    {
        bool fold;

        internal override bool overridePipelineAssetEditor => true;

        internal override void OnPipelineAssetEditorGUI(LightweightRenderPipelineAssetEditor pipelineAssetEditor)
        {
            pipelineAssetEditor.DrawQualitySettings();
            //EditorGUILayout.HelpBox("Settings from 2D Renderer Data:", MessageType.Info);

            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "2D Renderer Data");
            if (fold)
                DrawDefaultInspector();
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
