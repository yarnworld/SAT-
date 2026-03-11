using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using Common;

namespace UnityNativeHull
{
    // 表示一个原生的接触面（多点接触）
    public unsafe struct NativeManifold : IDisposable
    {
        public const int MaxPoints = 24; // 最大接触点数量
        public float3 Normal; // A -> B 之间的法向量

        private int _maxIndex; // 当前已添加的接触点索引

        private NativeBuffer _points; // 存储接触点的缓冲区

        // 将接触点缓冲区转换为数组形式
        public ContactPoint[] ToArray() => _points.ToArray<ContactPoint>(Length);

        // 获取指定索引的接触点
        public ContactPoint this[int i] => _points.GetItem<ContactPoint>(i);

        // 获取当前接触点数量
        public int Length => _maxIndex + 1;

        // 判断缓冲区是否已创建
        public bool IsCreated => _points.IsCreated;

        // 构造函数，初始化接触面缓冲区
        public NativeManifold(Allocator allocator)
        {
            _points = NativeBuffer.Create<ContactPoint>(MaxPoints, Allocator.Persistent);
            _maxIndex = -1;
            Normal = 0;
        }

        // 添加一个接触点，包含位置、距离和ID
        public void Add(float3 position, float distance, ContactID id)
        {
            Add(new ContactPoint
            {
                Id = id,
                Position = position,
                Distance = distance,
            });
        }

        // 添加一个接触点
        public void Add(ContactPoint cp)
        {
            if (_maxIndex >= MaxPoints)
                return;

            _points.SetItem(++_maxIndex, cp);
        }

        // 释放资源
        public void Dispose()
        {
            if (_points.IsCreated)
            {
                _points.Dispose();
            }
        }
    }

    // 定义接触点数据结构
    public struct ContactPoint
    {
        public ContactID Id; // 接触点的ID

        /// <summary>
        /// 形状第一次碰撞时的接触点位置。
        /// （即每个形状上两点之间的连线的中点）。
        /// </summary>
        public float3 Position;
        
        public float Distance; // 两形状之间的距离

        public float3 Penetration; // 穿透深度
        
    }

    // 接触点的ID，包含了一个FeaturePair和一个键值
    public struct ContactID
    {
        // FeaturePair表示两个形状之间的特征对
        public FeaturePair FeaturePair;
    }

    // 特征对，用于标识两个形状的边缘
    public struct FeaturePair
    {
        public int InEdge1; // 形状1的输入边
        public int OutEdge1; // 形状1的输出边
        public int InEdge2; // 形状2的输入边
        public int OutEdge2; // 形状2的输出边
    }
}
