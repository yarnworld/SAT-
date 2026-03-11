using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Common;

namespace UnityNativeHull
{
    // 表示一个原生的几何体（如多面体或凸包）
    public unsafe struct NativeHull : IDisposable
    {
        public int VertexCount;  // 顶点数量
        public int FaceCount;    // 面数量
        public int EdgeCount;    // 边数量

        // Unity 为 NativeArray、NativeList 等 Native 容器 提供了一种叫做 泄漏检测（Leak Detection） 的机制，
        // 以下用于告诉 NativeArray 在构造时跳过 LeakDetection 注册，用来提高速度。
        public NativeArrayNoLeakDetection<float3> VerticesNative;  // 顶点数组
        public NativeArrayNoLeakDetection<NativeFace> FacesNative;  // 面数组
        public NativeArrayNoLeakDetection<NativePlane> PlanesNative;  // 平面数组
        public NativeArrayNoLeakDetection<NativeHalfEdge> EdgesNative;  // 半边数组

        // 以下是直接指向内存的指针，供高效访问使用。
        [NativeDisableUnsafePtrRestriction]
        public float3* Vertices;  // 顶点指针

        [NativeDisableUnsafePtrRestriction]
        public NativeFace* Faces;  // 面指针

        [NativeDisableUnsafePtrRestriction]
        public NativePlane* Planes;  // 平面指针

        [NativeDisableUnsafePtrRestriction]
        public NativeHalfEdge* Edges;  // 半边指针

        private int _isCreated;  // 标记该结构体是否已创建
        private int _isDisposed; // 标记该结构体是否已释放

        // 判断结构体是否已创建
        public bool IsCreated
        {
            get => _isCreated == 1;
            set => _isCreated = value ? 1 : 0;
        }

        // 判断结构体是否已释放
        public bool IsDisposed
        {
            get => _isDisposed == 1;
            set => _isDisposed = value ? 1 : 0;
        }

        // 判断该结构体是否有效（即已创建且未释放）
        public bool IsValid => IsCreated && !IsDisposed;

        // 释放内存
        public void Dispose()
        {
            if (_isDisposed == 0)
            {
                _isDisposed = 1;

                if (VerticesNative.IsCreated)
                    VerticesNative.Dispose();

                if (FacesNative.IsCreated)
                    FacesNative.Dispose();

                if (PlanesNative.IsCreated)
                    PlanesNative.Dispose();

                if (EdgesNative.IsCreated)
                    EdgesNative.Dispose();

                Vertices = null;
                Faces = null;
                Planes = null;
                Edges = null;
            }
        }

        // 获取顶点
        public unsafe float3 GetVertex(int index) => VerticesNative[index];

        // 获取半边
        public unsafe NativeHalfEdge GetEdge(int index) => EdgesNative[index];

        // 获取半边引用
        public unsafe ref NativeHalfEdge GetEdgeRef(int index) => ref *(Edges + index);

        // 获取半边指针
        public unsafe NativeHalfEdge* GetEdgePtr(int index) => Edges + index;

        // 获取面（有限的真实的面，具体的矩形，三角形之类的面）
        public unsafe NativeFace GetFace(int index) => FacesNative[index];
        
        // 获取面指针
        public unsafe NativeFace* GetFacePtr(int index) => Faces + index;

        // 获取平面（面所在的大平面，无限的）
        public unsafe NativePlane GetPlane(int index) => PlanesNative[index];

        // 获取指定方向的支持顶点
        public unsafe float3 GetSupport(float3 direction)
        {
            return Vertices[GetSupportIndex(direction)];
        }

        // 获取指定方向的支持顶点索引
        public unsafe int GetSupportIndex(float3 direction)
        {
            int index = 0;
            float max = math.dot(direction, Vertices[index]);
            for (int i = 1; i < VertexCount; ++i)
            {
                float dot = math.dot(direction, Vertices[i]);
                if (dot > max)
                {
                    index = i;
                    max = dot;
                }
            }
            return index;
        }
    }

    // 扩展方法类
    public static class NativeHullExtensions
    {
        // 计算面质心
        //质心（centroid）就是面中各顶点的平均位置，是该面“几何中心”。
        public static float3 CalculateFaceCentroid(this NativeHull hull, NativeFace face)
        {
            float3 centroid = 0;
            int edgeCount = 0;
            ref NativeHalfEdge start = ref hull.GetEdgeRef(face.Edge);
            ref NativeHalfEdge current = ref start;
            
            //做个循环取到每一个顶点，最后再去取平均值即可
            do
            {
                edgeCount++;
                centroid += hull.GetVertex(current.Origin);
                current = ref hull.GetEdgeRef(current.Next);
            }
            while (current.Origin != start.Origin);
            return centroid / edgeCount;
        }
    }
}
