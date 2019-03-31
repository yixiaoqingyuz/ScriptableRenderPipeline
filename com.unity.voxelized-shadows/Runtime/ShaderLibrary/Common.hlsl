#ifndef UNITY_VX_SHADOWMAPS_COMMON_INCLUDED
#define UNITY_VX_SHADOWMAPS_COMMON_INCLUDED


StructuredBuffer<uint> _VxShadowMapsBuffer;


uint emulateCLZ(uint x)
{
    // emulate it similar to count leading zero.
    // count leading 1bit.

    uint n = 32;
    uint y;

    y = x >> 16; if (y != 0) { n = n - 16; x = y; }
    y = x >>  8; if (y != 0) { n = n -  8; x = y; }
    y = x >>  4; if (y != 0) { n = n -  4; x = y; }
    y = x >>  2; if (y != 0) { n = n -  2; x = y; }
    y = x >>  1; if (y != 0) return n - 2;

    return n - x;
}


// todo : calculate uint2 and more?
uint CalculateRescale(uint srcPosbit, uint dstPosbit)
{
    return 32 - emulateCLZ(srcPosbit ^ dstPosbit);
}


uint4 TraverseVxShadowMapPosQ(uint begin, uint3 posQ)
{
    uint attribute = begin + 18;
    uint maxScale = _VxShadowMapsBuffer[1];

    uint nodeIndex = 0;
    uint scale = maxScale;

    bool lit = false;
    bool shadowed = false;
    bool intersected = true;

    for (; scale > 3 && intersected; --scale)
    {
        // calculate where to go to child
        uint3 childDet = (posQ >> (scale - 1)) & 0x00000001;
        uint cellShift = (childDet.x << 1) + (childDet.y << 2) + (childDet.z << 3);
        uint cellbit   = 0x00000003 << cellShift;

        // calculate bit
        uint header = _VxShadowMapsBuffer[attribute + nodeIndex];
        uint childmask = header >> 16;
        uint shadowbit = (childmask & cellbit) >> cellShift;

        // determine whether it is lit or shadowed.
        lit      = shadowbit & 0x00000001;
        shadowed = shadowbit & 0x00000002;

        // if it has lit and shadowed, it is not decided yet(need to traverse more)
        intersected = lit && shadowed;

        // find next child node
        uint mask = ~(0xFFFFFFFF << cellShift);
        uint childrenbit = childmask & ((childmask & 0x0000AAAA) >> 1);
        uint childIndex = countbits(childrenbit & mask);

        // go down to the next node
        nodeIndex = _VxShadowMapsBuffer[attribute + nodeIndex + 1 + childIndex];
    }

    return uint4(nodeIndex, lit, shadowed, intersected);
}


uint2 TraverseVxShadowMapLeaf(uint begin, uint posQ_z, uint4 innerResult)
{
    uint attribute   = begin + 18;
    uint nodeIndex   = innerResult.x;

    bool lit         = innerResult.y;
    bool intersected = innerResult.w;

    uint bitmask0 = lit ? 0x00000000 : 0xFFFFFFFF;
    uint bitmask1 = lit ? 0x00000000 : 0xFFFFFFFF;

    if (intersected)
    {
        int childIndex = posQ_z % 8;
        int leafIndex = _VxShadowMapsBuffer[attribute + nodeIndex + childIndex];

        bitmask0 = _VxShadowMapsBuffer[attribute + leafIndex];
        bitmask1 = _VxShadowMapsBuffer[attribute + leafIndex + 1];
    }

    return uint2(bitmask0, bitmask1);
}


float PointSampleShadowBitmask(uint2 bitmask2, uint3 posQ)
{
    uint2 posLeaf = posQ.xy % uint2(8, 8);
    uint bitmask = posLeaf.y < 4 ? bitmask2.x : bitmask2.y;

    uint shift = posLeaf.x + 8 * (posLeaf.y % 4);
    uint mask = 0x00000001 << shift;

    float litRate = (bitmask & mask) == 0 ? 1.0 : 0.0;

    return litRate;
}


float PointSampleShadowBitmask(uint2 bitmask2, uint3 posQ, uint2 offset)
{
    uint2 posLeaf = (posQ.xy + offset) % uint2(8, 8);
    uint bitmask = posLeaf.y < 4 ? bitmask2.x : bitmask2.y;

    uint shift = posLeaf.x + 8 * (posLeaf.y % 4);
    uint mask = 0x00000001 << shift;

    float litRate = (bitmask & mask) == 0 ? 1.0 : 0.0;

    return litRate;
}


float PointSampleVxShadowing(uint begin, float3 positionWS)
{
    uint voxelResolution = _VxShadowMapsBuffer[begin];
    float4x4 worldToShadowMatrix =
    {
        asfloat(_VxShadowMapsBuffer[begin + 2]),
        asfloat(_VxShadowMapsBuffer[begin + 3]),
        asfloat(_VxShadowMapsBuffer[begin + 4]),
        asfloat(_VxShadowMapsBuffer[begin + 5]),

        asfloat(_VxShadowMapsBuffer[begin + 6]),
        asfloat(_VxShadowMapsBuffer[begin + 7]),
        asfloat(_VxShadowMapsBuffer[begin + 8]),
        asfloat(_VxShadowMapsBuffer[begin + 9]),

        asfloat(_VxShadowMapsBuffer[begin + 10]),
        asfloat(_VxShadowMapsBuffer[begin + 11]),
        asfloat(_VxShadowMapsBuffer[begin + 12]),
        asfloat(_VxShadowMapsBuffer[begin + 13]),

        asfloat(_VxShadowMapsBuffer[begin + 14]),
        asfloat(_VxShadowMapsBuffer[begin + 15]),
        asfloat(_VxShadowMapsBuffer[begin + 16]),
        asfloat(_VxShadowMapsBuffer[begin + 17]),
    };

    float3 posNDC = mul(worldToShadowMatrix, float4(positionWS, 1.0)).xyz;
    float3 posP = posNDC * (float)voxelResolution;
    uint3  posQ = (uint3)posP;

    if (any(posQ >= (voxelResolution.xxx - 1)))
        return 1;

    uint4 result = TraverseVxShadowMapPosQ(begin, posQ);
    if (result.w == 0)
        return result.y ? 1 : 0;

    uint2 bitmask2 = TraverseVxShadowMapLeaf(begin, posQ.z, result);
    float attenuation = PointSampleShadowBitmask(bitmask2, posQ);

    return attenuation;
}


#endif // UNITY_VX_SHADOWMAPS_COMMON_INCLUDED
