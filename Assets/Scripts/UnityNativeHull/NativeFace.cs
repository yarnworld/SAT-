using System.Diagnostics;
using Unity.Mathematics;

namespace UnityNativeHull
{
    // 在调试时，显示 NativeFace 结构体的 Edge 字段
    [DebuggerDisplay("NativeFace: Edge={Edge}")]
    public struct NativeFace
    {
        /// <summary>
        /// 该面（face）上的起始边的索引。
        /// </summary>
        public int Edge;
    };
}