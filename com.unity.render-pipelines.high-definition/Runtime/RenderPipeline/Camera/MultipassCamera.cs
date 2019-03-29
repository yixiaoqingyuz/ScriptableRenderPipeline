using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    // Empty interface used to override pass behavior and hash code
    public interface ICameraPass {}

    // Used as key in Dictionary
    public struct MultipassCamera : IEquatable<MultipassCamera>
    {
        public  readonly Camera      camera;
        public  readonly ICameraPass cameraPass;
        private readonly int         cachedHashCode;

        public MultipassCamera(Camera camera, ICameraPass cameraPass = null)
        {
            this.camera     = camera;
            this.cameraPass = cameraPass;
            cachedHashCode  = ComputeHashCode(camera, cameraPass);
        }

        public static bool operator ==(MultipassCamera x, MultipassCamera y) => x.cachedHashCode == y.cachedHashCode;
        public static bool operator !=(MultipassCamera x, MultipassCamera y) => x.cachedHashCode != y.cachedHashCode;
        public bool Equals(MultipassCamera other) => cachedHashCode == other.cachedHashCode;
        public override bool Equals(object obj) => obj is MultipassCamera && ((MultipassCamera)obj).cachedHashCode == cachedHashCode;
        public override int GetHashCode() => cachedHashCode;

        static int ComputeHashCode(Camera camera, ICameraPass cameraPass)
        {
            int hash = 13;

            unchecked
            {
                hash = hash * 23 + camera.GetHashCode();

                if (cameraPass != null)
                    hash = hash * 23 + cameraPass.GetHashCode();
            }

            return hash;
        }
    }
}
