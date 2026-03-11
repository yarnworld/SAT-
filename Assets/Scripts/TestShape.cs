using System;
using System.Diagnostics;
using Unity.Burst;
using UnityNativeHull;

[DebuggerDisplay("TestShape: Id={Id}")]
[BurstCompile]  // 启用 Burst 编译器优化此结构体的性能
//IEquatable是为了重载Equals判等操作，而IComparable是为了CompareTo比较
public struct TestShape : IEquatable<TestShape>, IComparable<TestShape>
{
    // 唯一标识符
    public int Id;
    // 形状的外壳（例如多面体）
    public NativeHull Hull;
    
    // 实现 IEquatable 接口的 Equals 方法，用于比较两个 TestShape 是否相等
    public bool Equals(TestShape other)
    {
        return Id == other.Id;
    }

    // 重写对象的 Equals 方法，用于与其他对象比较是否相等
    public override bool Equals(object obj)
    {
        return obj is TestShape shape && shape.Equals(this);
    }

    // 实现 IComparable 接口的 CompareTo 方法，用于根据 Id 对 TestShape 进行比较
    public int CompareTo(TestShape other)
    {
        return Id.CompareTo(other.Id);
    }

    // 重写 GetHashCode 方法，用于为 TestShape 生成哈希码
    public override int GetHashCode()
    {
        return Id;
    }
}