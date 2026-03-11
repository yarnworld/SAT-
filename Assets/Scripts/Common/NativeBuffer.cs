// NativeBuffer<T> 是一个类似于 NativeArray<T> 的结构，支持在 Burst Job 中安全使用
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Common
{
    /// <summary>
    /// NativeBuffer<T> 是 NativeArray<T> 的替代品，
    /// 由于 NativeList<T> 在 Burst Job 中实例化存在问题
    /// </summary>
    [NativeContainer]
    [DebuggerDisplay("Length = {Length}")]
    [DebuggerTypeProxy(typeof(NativeBufferDebugView<>))]
    public struct NativeBuffer<T> : IDisposable where T : struct
    {
        private NativeBuffer _buffer;
        private int _maxIndex; // 当前添加元素的最大索引

        /// <summary>
        /// 使用已有的内存（如 stackalloc）初始化缓冲区
        /// </summary>
        public unsafe NativeBuffer(void* ptr, int elementCount)
        {
            _buffer = NativeBuffer.Assign<T>(ptr, elementCount);
            _maxIndex = -1;
        }

        /// <summary>
        /// 使用分配器动态创建缓冲区
        /// </summary>
        public NativeBuffer(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            _buffer = NativeBuffer.Create<T>(length, allocator, options);
            _maxIndex = -1;
        }

        public ref T this[int i] => ref _buffer.AsRef<T>(i);

        public int Add(T item)
        {
            _buffer.SetItem(++_maxIndex, item);
            return _maxIndex;
        }

        public int Length => _maxIndex + 1;

        public bool IsCreated => _buffer.IsCreated;

        public T[] ToArray() => _buffer.ToArray<T>(Length);

        public void Dispose() => _buffer.Dispose();
    }

    // 调试器中用于显示内容
    internal sealed class NativeBufferDebugView<T> where T : struct
    {
        private NativeBuffer<T> Buffer;

        public NativeBufferDebugView(NativeBuffer<T> buffer)
        {
            Buffer = buffer;
        }

        public T[] Items => Buffer.ToArray();
    }

    /// <summary>
    /// NativeBuffer 是底层非泛型结构，用于手动管理原始内存块
    /// </summary>
    [NativeContainer]
    [DebuggerDisplay("Length = {Length}")]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct NativeBuffer : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        internal unsafe void* m_Buffer;
        internal int m_Length;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        internal int itemSize;
        internal int m_AllocatorLabel;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public int Length => m_Length;

        public static unsafe NativeBuffer Assign(void* ptr, int itemSize, int length)
        {
            NativeBuffer buffer;
            buffer.m_Buffer = ptr;
            buffer.m_Length = length;
            buffer.itemSize = itemSize;
            buffer.m_MinIndex = 0;
            buffer.m_MaxIndex = length - 1;
            buffer.m_AllocatorLabel = (int)Allocator.Invalid;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            buffer.m_Safety = AtomicSafetyHandle.Create();
#endif
            return buffer;
        }

        public static unsafe NativeBuffer Assign<T>(void* ptr, int length) where T : struct
        {
            return Assign(ptr, UnsafeUtility.SizeOf<T>(), length);
        }

        public static unsafe NativeBuffer Create<T>(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : struct
        {
            Allocate<T>(length, allocator, out NativeBuffer buffer);

            if ((options & NativeArrayOptions.ClearMemory) != NativeArrayOptions.ClearMemory)
                return buffer;

            buffer.Clear();
            return buffer;
        }

        public unsafe void Clear()
        {
            UnsafeUtility.MemClear(m_Buffer, (long)Length * itemSize);
        }

        private static unsafe void Allocate<T>(int length, Allocator allocator, out NativeBuffer array) where T : struct
        {
            IsBlittableAndThrow<T>();
            Allocate(length, UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator, out array);
        }

        private static unsafe void Allocate(int length, int itemSize, int align, Allocator allocator, out NativeBuffer array)
        {
            long size = itemSize * (long)length;
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0");
            if (size > (long)int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(length), $"Length * sizeof(T) cannot exceed {int.MaxValue} bytes");

            array.m_Buffer = UnsafeUtility.Malloc(size, align, allocator);
            array.m_Length = length;
            array.m_AllocatorLabel = (int)allocator;
            array.m_MinIndex = 0;
            array.m_MaxIndex = length - 1;
            array.itemSize = itemSize;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            array.m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        [BurstDiscard]
        internal static void IsBlittableAndThrow<T>() where T : struct
        {
            if (!UnsafeUtility.IsBlittable<T>())
                throw new InvalidOperationException($"{typeof(T)} used in NativeBuffer must be blittable.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void CheckElementReadAccess(int index)
        {
            if (index < m_MinIndex || index > m_MaxIndex)
                FailOutOfRangeError(index);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void CheckElementWriteAccess(int index)
        {
            if (index < m_MinIndex || index > m_MaxIndex)
                FailOutOfRangeError(index);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        public unsafe T GetItem<T>(int index)
        {
            CheckElementReadAccess(index);
            return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
        }

        public unsafe ref T AsRef<T>(int index) where T : struct
        {
            return ref UnsafeUtility.AsRef<T>((void*)((IntPtr)m_Buffer + (UnsafeUtility.SizeOf<T>() * index)));
        }

        public unsafe void SetItem<T>(int index, T value)
        {
            CheckElementWriteAccess(index);
            UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
        }

        public unsafe bool IsCreated => (IntPtr)m_Buffer != IntPtr.Zero;

        [WriteAccessRequired]
        public unsafe void Dispose()
        {
            if (!UnsafeUtility.IsValidAllocator((Allocator)m_AllocatorLabel))
                throw new InvalidOperationException($"The NativeBuffer cannot be disposed because it was not allocated with a valid allocator ({(Allocator)m_AllocatorLabel}).");

            UnsafeUtility.Free(m_Buffer, (Allocator)m_AllocatorLabel);
            m_Buffer = null;
            m_Length = 0;
        }

        public T[] ToArray<T>(int length) where T : struct
        {
            T[] dst = new T[length];
            Copy(this, dst, length);
            return dst;
        }

        private void FailOutOfRangeError(int index)
        {
            if (index < Length && (m_MinIndex != 0 || m_MaxIndex != Length - 1))
                throw new IndexOutOfRangeException($"Index {index} is out of restricted IJobParallelFor range [{m_MinIndex}...{m_MaxIndex}] in NativeBuffer.");
            throw new IndexOutOfRangeException($"Index {index} is out of range of '{Length}' Length.");
        }

        public static void Copy<T>(NativeBuffer src, T[] dst, int length) where T : struct
        {
            Copy(src, 0, dst, 0, length);
        }

        public static unsafe void Copy<T>(NativeBuffer src, int srcIndex, T[] dst, int dstIndex, int length) where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(src.m_Safety);
#endif
            if (dst == null)
                throw new ArgumentNullException(nameof(dst));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (srcIndex < 0 || srcIndex > src.Length || (srcIndex == src.Length && src.Length > 0))
                throw new ArgumentOutOfRangeException(nameof(srcIndex));
            if (dstIndex < 0 || dstIndex > dst.Length || (dstIndex == dst.Length && dst.Length > 0))
                throw new ArgumentOutOfRangeException(nameof(dstIndex));
            if (srcIndex + length > src.Length)
                throw new ArgumentException("length is too large for source buffer");
            if (dstIndex + length > dst.Length)
                throw new ArgumentException("length is too large for destination array");

            GCHandle gcHandle = GCHandle.Alloc(dst, GCHandleType.Pinned);
            UnsafeUtility.MemCpy(
                (void*)((IntPtr)gcHandle.AddrOfPinnedObject() + (dstIndex * UnsafeUtility.SizeOf<T>())),
                (void*)((IntPtr)src.m_Buffer + (srcIndex * UnsafeUtility.SizeOf<T>())),
                length * UnsafeUtility.SizeOf<T>());
            gcHandle.Free();
        }
    }
}