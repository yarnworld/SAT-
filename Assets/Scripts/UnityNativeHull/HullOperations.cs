using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Common;

namespace UnityNativeHull
{
    public static class HullOperations
    {
        /// <summary>
        /// 尝试获取两个凸包之间的接触信息
        /// </summary>
        [BurstCompile]
        public struct TryGetContact : IBurstRefAction<NativeManifold, RigidTransform, NativeHull, RigidTransform, NativeHull>
        {
            // 执行接触检测并返回接触点
            public void Execute(ref NativeManifold manifold, RigidTransform t1, NativeHull hull1, RigidTransform t2, NativeHull hull2)
            {
                HullIntersection.NativeHullHullContact(ref manifold, t1, hull1, t2, hull2);
            }

            // 静态方法，用于调用函数
            public static bool Invoke(out NativeManifold result, RigidTransform t1, NativeHull hull1, RigidTransform t2, NativeHull hull2)
            {
                // Burst作业只能分配为临时（Temp）内存
                result = new NativeManifold(Allocator.Persistent); 

                BurstRefAction<TryGetContact, NativeManifold, RigidTransform, NativeHull, RigidTransform, NativeHull>.Run(Instance, ref result, t1, hull1, t2, hull2);
                return result.Length > 0;
            }

            // TryGetContact 的实例
            public static TryGetContact Instance { get; } = new TryGetContact();
        }
    }
}
