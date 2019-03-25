using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.ShaderGraph.Drawing.Colors
{
    class ColorManager
    {
        List<IColorProvider> m_Providers;
        IColorProvider m_ActiveColors;

        public ColorManager(string activeColors)
        {
            m_Providers = new List<IColorProvider>();

            if (string.IsNullOrEmpty(activeColors))
                activeColors = "Category";

            foreach (var colorType in UnityEditor.TypeCache.GetTypesDerivedFrom<IColorProvider>())
            {
                var provider = (IColorProvider) Activator.CreateInstance(colorType);
                if (provider.Title == activeColors)
                {
                    m_ActiveColors = provider;
                }

                m_Providers.Add(provider);
            }
        }

        public Color GetColor(AbstractMaterialNode node)
        {
            return m_ActiveColors.GetColor(node);
        }
    }

    interface IColorProvider
    {
        string Title { get; }

        Color GetColor(AbstractMaterialNode node);
    }

    class CategoryColors : IColorProvider
    {
        public string Title => "Category";

        public Color GetColor(AbstractMaterialNode node)
        {
            var title = node.GetType().GetCustomAttributes(typeof(TitleAttribute), false).FirstOrDefault() as TitleAttribute;
            if(!typeColors.TryGetValue(title.title[0], out var ret))
                ret = Color.magenta;
            return ret;
        }

        public CategoryColors()
        {
            typeColors = new Dictionary<string, Color>();
            typeColors.Add("Artistic", Color.cyan);
            typeColors.Add("Channel", Color.yellow);
            typeColors.Add("Input", Color.green);
            typeColors.Add("Master", Color.black);
            typeColors.Add("Math", Color.red);
            typeColors.Add("Procedural", Color.blue);
            typeColors.Add("Utility", Color.white);
            typeColors.Add("UV", Color.gray);
            
//            typeColors.Add("Artistic", new Color());
//            typeColors.Add("Channel", new Color());
//            typeColors.Add("Input", new Color());
//            typeColors.Add("Master", new Color());
//            typeColors.Add("Math", new Color());
//            typeColors.Add("Procedural", new Color());
//            typeColors.Add("Utility", new Color());
//            typeColors.Add("UV", new Color());
        }
        Dictionary<string, Color> typeColors;
    }
}
