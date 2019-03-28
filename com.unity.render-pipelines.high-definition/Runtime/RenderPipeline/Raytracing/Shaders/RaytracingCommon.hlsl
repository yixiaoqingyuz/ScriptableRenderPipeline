// This array converts an index to the local coordinate shift of the half resolution texture
static const uint2 HalfResIndexToCoordinateShift[4] = { uint2(0,0), uint2(1, 0), uint2(0, 1), uint2(1, 1) };

// Mapping from roughness (GGX in particular) to ray spread angle
float roughnessToSpreadAngle(float roughness)
{
    // FIXME: The mapping will most likely need adjustment...
    return roughness * PI/8;
}

#define USE_RAY_CONE_LOD

float computeTextureLOD(Texture2D targetTexture, float4 uvMask, float2 tiling, float3 viewWS, float3 normalWS, float coneWidth, IntersectionVertice intersectionVertice)
{
    // First of all we need to grab the dimensions of the target texture
    uint texWidth, texHeight, numMips;
    targetTexture.GetDimensions(0, texWidth, texHeight, numMips);

    // Fetch the target area based on the mask
    float targetTexArea = uvMask.x * intersectionVertice.texCoord0Area
                        + uvMask.y * intersectionVertice.texCoord1Area
                        + uvMask.z * intersectionVertice.texCoord2Area
                        + uvMask.w * intersectionVertice.texCoord3Area;

    // Apply tiling factor to the tex coord area, and convert it to texel space
    targetTexArea *= texWidth * texHeight * tiling.x * tiling.y;

    // Compute final LOD following the ray cone formulation in Ray Tracing Gems (20.3.4)
    float lambda = 0.5 * log2(targetTexArea / intersectionVertice.triangleArea);
    lambda += log2(abs(coneWidth / dot(viewWS, normalWS)));

    return lambda;
}
