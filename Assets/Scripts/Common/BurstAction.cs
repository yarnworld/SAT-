using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Common
{
    
    // 定义一个基础接口，用于标识所有 Burst 操作
    public interface IBurstOperation
    {

    }
    
    // 定义一个接口，用于声明带有引用参数的操作
    public interface IBurstRefAction<T1, T2, T3, T4, T5> : IBurstOperation
    {
        // 执行方法，接受五个参数，其中第一个参数为引用类型
        void Execute(ref T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
    }

    // 一个 Burst 编译的结构体，执行带有引用参数的操作
    // 这个结构体实现了 IJob，允许在 Unity 作业系统中异步执行
    [BurstCompile]
    public struct BurstRefAction<TFunc, T1, T2, T3, T4, T5> : IJob
        where TFunc : struct, IBurstRefAction<T1, T2, T3, T4, T5> // TFunc 必须实现 IBurstRefAction 接口
        where T1 : unmanaged // T1 必须是非托管类型
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
    {
        // 使用 NativeDisableUnsafePtrRestriction 属性标记指针类型字段，
        // 防止 Unity 分配内存时对其进行额外的安全性检查
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* FunctionPtr; // 存储函数指针
        [NativeDisableUnsafePtrRestriction]
        public unsafe T1* Argument1Ptr; // 存储第一个参数的指针
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* Argument2Ptr; // 存储第二个参数的指针
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* Argument3Ptr; // 存储第三个参数的指针
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* Argument4Ptr; // 存储第四个参数的指针
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* Argument5Ptr; // 存储第五个参数的指针

        // 执行作业的核心方法
        public unsafe void Execute()
        {
            // 将函数指针转换为具体的 TFunc 类型
            //把 FunctionPtr 指向的原始内存数据，反序列化/复制为一个结构体 TFunc 的实例 func
            UnsafeUtility.CopyPtrToStructure(FunctionPtr, out TFunc func);

            // 将指针转换为相应类型的参数
            UnsafeUtility.CopyPtrToStructure(Argument2Ptr, out T2 arg2);
            UnsafeUtility.CopyPtrToStructure(Argument3Ptr, out T3 arg3);
            UnsafeUtility.CopyPtrToStructure(Argument4Ptr, out T4 arg4);
            UnsafeUtility.CopyPtrToStructure(Argument5Ptr, out T5 arg5);

            // 执行实际的操作，注意第一个参数是引用类型
            func.Execute(ref *Argument1Ptr, arg2, arg3, arg4, arg5);
        }

        // 静态方法，用于启动该作业
        public static unsafe void Run(TFunc func, ref T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {   
            // 创建一个 BurstRefAction 实例，并初始化所有字段
            new BurstRefAction<TFunc, T1, T2, T3, T4, T5>
            {
                //UnsafeUtility.AddressOf(...)	返回 func 在内存中的地址,把地址赋给 Job 的 FunctionPtr 字段，
                //便于 Job 内部后续还原为原始对象
                FunctionPtr = UnsafeUtility.AddressOf(ref func),
                Argument1Ptr = (T1*)UnsafeUtility.AddressOf(ref arg1),
                Argument2Ptr = UnsafeUtility.AddressOf(ref arg2),
                Argument3Ptr = UnsafeUtility.AddressOf(ref arg3),
                Argument4Ptr = UnsafeUtility.AddressOf(ref arg4),
                Argument5Ptr = UnsafeUtility.AddressOf(ref arg5),
            }.Run(); // 调用 Run 方法执行作业
        }
    }
}
