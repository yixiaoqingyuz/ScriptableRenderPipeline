using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;
using System.Linq;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    public class HDLitGUI : ShaderGUI
    {
        int FindNeutralSortingPriorityForRenderQueue(int renderQueue)
        {
            if (HDRenderQueue.k_RenderQueue_PreRefraction.Contains(renderQueue))
                return (int)HDRenderQueue.Priority.PreRefraction;
            if (HDRenderQueue.k_RenderQueue_Transparent.Contains(renderQueue))
                return (int)HDRenderQueue.Priority.Transparent;
            if (HDRenderQueue.k_RenderQueue_LowTransparent.Contains(renderQueue))
                return (int)HDRenderQueue.Priority.LowTransparent;
            if (HDRenderQueue.k_RenderQueue_AfterPostProcessTransparent.Contains(renderQueue))
                return (int)HDRenderQueue.Priority.AfterPostprocessTransparent;
            return -1;
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            materialEditor.PropertiesDefaultGUI(props);
            if (materialEditor.EmissionEnabledProperty())
            {
                materialEditor.LightmapEmissionFlagsProperty(MaterialEditor.kMiniTextureFieldLabelIndentLevel, true, true);
            }

            // Check if every selected material is transparent
            bool displaySortingPriority = materialEditor.targets.All(o => HDRenderQueue.k_RenderQueue_AllTransparent.Contains((o as Material).renderQueue));
            int firstMaterialRenderQueue = (materialEditor.target as Material).renderQueue;
            // The material inspector does not support editing multiple materials when the have different shaders,
            // thus all the render queue type must be the same for all selected materials, so it's fine to use the neutral
            // sorting priority of the first material
            int neutralRenderQueue = FindNeutralSortingPriorityForRenderQueue(firstMaterialRenderQueue);
            
            foreach (var obj in materialEditor.targets)
            {
                var material = (Material)obj;

                if (firstMaterialRenderQueue != material.renderQueue)
                    EditorGUI.showMixedValue = true;
            }

            if (displaySortingPriority)
            {
                using (var change = new EditorGUI.ChangeCheckScope())
                {
                    int newRenderQueue = (int)EditorGUILayout.FloatField("Sorting Priority", firstMaterialRenderQueue - neutralRenderQueue);

                    newRenderQueue = HDRenderQueue.ClampsTransparentRangePriority(newRenderQueue);

                    // If we changed the renderqueue, we set it to every selected material
                    if (change.changed)
                    {
                        foreach (var obj in materialEditor.targets)
                            (obj as Material).renderQueue = newRenderQueue + neutralRenderQueue;
                    }
                }
            }
            
            EditorGUI.showMixedValue = false;

            // Make sure all selected materials are initialized.
            string materialTag = "MotionVector";
            foreach (var obj in materialEditor.targets)
            {
                var material = (Material)obj;
                string tag = material.GetTag(materialTag, false, "Nothing");
                if (tag == "Nothing")
                {
                    material.SetShaderPassEnabled(HDShaderPassNames.s_MotionVectorsStr, false);
                    material.SetOverrideTag(materialTag, "User");
                }
            }

            {
                // If using multi-select, apply toggled material to all materials.
                bool enabled = ((Material)materialEditor.target).GetShaderPassEnabled(HDShaderPassNames.s_MotionVectorsStr);
                EditorGUI.BeginChangeCheck();
                enabled = EditorGUILayout.Toggle("Motion Vector For Vertex Animation", enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var obj in materialEditor.targets)
                    {
                        var material = (Material)obj;
                        material.SetShaderPassEnabled(HDShaderPassNames.s_MotionVectorsStr, enabled);
                    }
                }
            }
            
            if (DiffusionProfileMaterialUI.IsSupported(materialEditor))
                DiffusionProfileMaterialUI.OnGUI(FindProperty("_DiffusionProfileAsset", props), FindProperty("_DiffusionProfileHash", props));
        }
    }
}
