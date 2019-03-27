using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    // Used as key in Dictionary
    public struct MultipassCamera : IEquatable<MultipassCamera>
    {
        private readonly Camera m_Camera;
        private readonly int    m_PassId;
        private readonly int    m_CachedHashCode;

        public MultipassCamera(Camera camera, int passId = -1)
        {
            m_Camera = camera;
            m_PassId = passId;

            // Compute the hash code once and store it (all variables must be readonly)
            // Note: camera can be deleted at any time and without caching,
            // the hash code could change and impact the usage of this struct as Key in a Dictionary
            m_CachedHashCode = ComputeHashCode(camera, passId);
        }

        public Camera camera { get { return m_Camera; } }
        public int    passId { get { return m_PassId; } }

        public bool Equals(MultipassCamera other)
        {
            return passId == other.passId && camera == other.camera;
        }

        public override bool Equals(object obj)
        {
            if (obj is MultipassCamera)
                return Equals((MultipassCamera)obj);

            return false;
        }

        public static bool operator == (MultipassCamera x, MultipassCamera y)
        {
            return x.Equals(y);
        }

        public static bool operator != (MultipassCamera x, MultipassCamera y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return m_CachedHashCode;
        }

        private static int ComputeHashCode(Camera camera, int passId)
        {
            int hash = 13;

            unchecked
            {
                hash = hash * 23 + passId;
                hash = hash * 23 + camera.GetHashCode();
            }

            return hash;
        }
    }
}
