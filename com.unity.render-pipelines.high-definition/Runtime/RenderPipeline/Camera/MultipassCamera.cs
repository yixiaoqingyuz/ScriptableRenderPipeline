using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    // Used as key in Dictionary
    public struct MultipassCamera : IEquatable<MultipassCamera>
    {
        public readonly Camera camera;
        public readonly XRPass pass;

        private readonly int cachedHashCode;

        public MultipassCamera(Camera camera, XRPass pass = null)
        {
            this.camera = camera;
            this.pass = pass;
            cachedHashCode = ComputeHashCode(camera, pass);
        }

        public static bool operator ==(MultipassCamera x, MultipassCamera y) => x.cachedHashCode == y.cachedHashCode;
        public static bool operator !=(MultipassCamera x, MultipassCamera y) => x.cachedHashCode != y.cachedHashCode;
        public override bool Equals(object obj) => obj is MultipassCamera && ((MultipassCamera)obj).cachedHashCode == cachedHashCode;
        public bool Equals(MultipassCamera other) => cachedHashCode == other.cachedHashCode;
        public override int GetHashCode() => cachedHashCode;

        private static int ComputeHashCode(Camera camera, XRPass pass)
        {
            int hash = 13;

            unchecked
            {
                hash = hash * 23 + camera.GetHashCode();
                hash = hash * 23 + pass.GetHashCode();
            }

            return hash;
        }
    }
}
