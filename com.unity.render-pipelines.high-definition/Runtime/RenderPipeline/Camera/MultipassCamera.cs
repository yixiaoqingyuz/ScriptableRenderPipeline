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

        readonly int cachedHashCode;

        public MultipassCamera(Camera camera, XRPass pass)
        {
            this.camera = camera;
            this.pass = pass;
            cachedHashCode = ComputeHashCode(camera, pass);
        }

        public static bool operator ==(MultipassCamera x, MultipassCamera y) => x.cachedHashCode == y.cachedHashCode;
        public static bool operator !=(MultipassCamera x, MultipassCamera y) => x.cachedHashCode != y.cachedHashCode;
        public bool Equals(MultipassCamera other) => cachedHashCode == other.cachedHashCode;
        public override bool Equals(object obj) => obj is MultipassCamera && ((MultipassCamera)obj).cachedHashCode == cachedHashCode;
        public override int GetHashCode() => cachedHashCode;

        static int ComputeHashCode(Camera camera, XRPass pass)
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
