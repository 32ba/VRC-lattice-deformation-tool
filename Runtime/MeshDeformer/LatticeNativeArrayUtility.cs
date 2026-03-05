using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal static class LatticeNativeArrayUtility
    {
        public static NativeArray<float3> CreateCopy(Vector3[] source, Allocator allocator)
        {
            int length = source?.Length ?? 0;
            var array = CreateFloat3Array(length, allocator);

            if (length == 0 || source == null)
            {
                return array;
            }

            for (int i = 0; i < length; i++)
            {
                Vector3 value = source[i];
                array[i] = new float3(value.x, value.y, value.z);
            }

            return array;
        }

        public static NativeArray<T> CreateCopy<T>(T[] source, Allocator allocator)
            where T : struct
        {
            int length = source?.Length ?? 0;
            var array = new NativeArray<T>(math.max(length, 0), allocator, NativeArrayOptions.UninitializedMemory);

            if (length == 0 || source == null)
            {
                return array;
            }

            for (int i = 0; i < length; i++)
            {
                array[i] = source[i];
            }

            return array;
        }

        public static NativeArray<float3> CreateFloat3Array(int length, Allocator allocator)
        {
            return new NativeArray<float3>(math.max(length, 0), allocator, NativeArrayOptions.UninitializedMemory);
        }

        public static void CopyFromManaged(this NativeArray<float3> destination, Vector3[] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureCopyLength(destination.Length, source.Length);

            for (int i = 0; i < source.Length; i++)
            {
                Vector3 value = source[i];
                destination[i] = new float3(value.x, value.y, value.z);
            }
        }

        public static void CopyToManaged(this NativeArray<float3> source, Vector3[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            EnsureCopyLength(source.Length, destination.Length);

            for (int i = 0; i < destination.Length; i++)
            {
                float3 value = source[i];
                destination[i] = new Vector3(value.x, value.y, value.z);
            }
        }

        public static void CopyToManaged<T>(this NativeArray<T> source, T[] destination)
            where T : struct
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            EnsureCopyLength(source.Length, destination.Length);

            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        private static void EnsureCopyLength(int sourceLength, int destinationLength)
        {
            if (sourceLength != destinationLength)
            {
                throw new ArgumentException("Source and destination lengths must match for copy operations.");
            }
        }
    }
}

