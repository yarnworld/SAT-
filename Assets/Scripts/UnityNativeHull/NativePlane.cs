using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace UnityNativeHull
{
    // NativePlane 结构体用于表示一个平面
    [DebuggerDisplay("NativePlane: {Normal}, {Offset}")]
    public unsafe struct NativePlane
    {
        //平面方程可以表示为Normal⋅P=Offset
        // Normal 是平面的法向量。
        // P 是平面上的任意一点。
        // Offset 是从原点（0, 0, 0）到平面的距离，沿着法线方向的距离。
        
        /// <summary>
        /// 平面的法向量，表示平面相对于 hull 原点的方向。
        /// </summary>
        public float3 Normal;

        /// <summary>
        /// 平面到 hull 原点的距离。
        /// </summary>
        public float Offset;

        // 构造函数，初始化平面的法向量和偏移量
        public NativePlane(float3 normal, float offset)
        {
            Normal = normal;
            Offset = offset;    
        }

        // 计算某一点到平面的距离
        public float Distance(float3 point)
        {
            //从原点算的这个point去和法线做点乘，当然要减去这个Offset了
            return dot(Normal, point) - Offset;
        }

        // 计算某一点到平面的最近点
        public float3 ClosestPoint(float3 point)
        {
            return point - Distance(point) * normalize(Normal);
        }

        // 通过刚体变换来转换平面
        public static NativePlane operator *(RigidTransform t, NativePlane plane)
        {
            // 对平面的法向量进行旋转变换
            float3 normal = mul(t.rot, plane.Normal);
            // 返回平面的位置和法向量经过刚体变换后的平面,新的偏移量 = 旧的偏移量 + 新法向量与刚体平移部分的点积
            return new NativePlane(normal, plane.Offset + dot(normal, t.pos));
        }
    };
}