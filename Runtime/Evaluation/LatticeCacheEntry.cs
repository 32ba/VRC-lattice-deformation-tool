using System;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    [Serializable]
    internal struct LatticeCacheEntry
    {
        public int Corner0;
        public int Corner1;
        public int Corner2;
        public int Corner3;
        public int Corner4;
        public int Corner5;
        public int Corner6;
        public int Corner7;
        public float4 Weights0;
        public float4 Weights1;
        public float3 Barycentric;
        public float3 NormalizedCoordinate;
    }
}
