using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Experimental.Rendering
{
    public sealed class RenderGraphTempPool
    {
        Dictionary<(Type, int), Stack<object>>  m_ArrayPool = new Dictionary<(Type, int), Stack<object>>();
        List<(object, (Type, int))>             m_AllocatedArrays = new List<(object, (Type, int))>();

        Stack<MaterialPropertyBlock>            m_MaterialPropertyBlockPool = new Stack<MaterialPropertyBlock>();
        List<MaterialPropertyBlock>             m_AllocatedMaterialPropertyBlocks = new List<MaterialPropertyBlock>();

        internal RenderGraphTempPool() { }

        public T[] GetTempArray<T>(int size)
        {
            if (!m_ArrayPool.TryGetValue((typeof(T), size), out var stack))
            {
                stack = new Stack<object>();
                m_ArrayPool.Add((typeof(T), size), stack);
            }

            var result = stack.Count > 0 ? (T[])stack.Pop() : new T[size];
            m_AllocatedArrays.Add((result, (typeof(T), size)));
            return result;
        }

        public MaterialPropertyBlock GetTempMaterialPropertyBlock()
        {
            var result = m_MaterialPropertyBlockPool.Pop() ?? new MaterialPropertyBlock();
            result.Clear();
            m_AllocatedMaterialPropertyBlocks.Add(result);
            return result;
        }

        internal void ReleaseAllTempAlloc()
        {
            foreach(var arrayDesc in m_AllocatedArrays)
            {
                bool result = m_ArrayPool.TryGetValue(arrayDesc.Item2, out var stack);
                Debug.Assert(result, "Correct stack type should always be allocated.");
                stack.Push(arrayDesc.Item1);
            }

            m_AllocatedArrays.Clear();

            foreach(var mpb in m_AllocatedMaterialPropertyBlocks)
            {
                m_MaterialPropertyBlockPool.Push(mpb);
            }

            m_AllocatedMaterialPropertyBlocks.Clear();
        }
    }
}
