#if UNITY_EDITOR
using System;
using NUnit.Framework;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Guards allocation assertions against Unity runtimes where Mono exposes
    /// GC.GetAllocatedBytesForCurrentThread as a non-advancing stub.
    /// Interactive release evidence still comes from the Unity Profiler.
    /// </summary>
    internal static class ManagedAllocationCounter
    {
        private static bool? s_isSupported;
        private static bool s_reportedUnsupported;

        internal static bool IsSupported
        {
            get
            {
                if (s_isSupported.HasValue) return s_isSupported.Value;

                long before = GC.GetAllocatedBytesForCurrentThread();
                var calibration = new byte[16 * 1024];
                GC.KeepAlive(calibration);
                long after = GC.GetAllocatedBytesForCurrentThread();
                s_isSupported = after > before;
                return s_isSupported.Value;
            }
        }

        internal static void AssertNoAllocations(long allocated, string message = null)
        {
            if (IsSupported)
            {
                Assert.That(allocated, Is.Zero, message);
                return;
            }

            ReportUnsupportedOnce();
        }

        internal static void AssertEqualAllocations(long first, long second, string message = null)
        {
            if (IsSupported)
            {
                Assert.That(second, Is.EqualTo(first), message);
                return;
            }

            ReportUnsupportedOnce();
        }

        internal static void AssertLessThan(long allocated, long limit, string message = null)
        {
            if (IsSupported)
            {
                Assert.That(allocated, Is.LessThan(limit), message);
                return;
            }

            ReportUnsupportedOnce();
        }

        internal static string Format(long allocated)
        {
            return IsSupported ? $"{allocated} B" : "counter unavailable";
        }

        private static void ReportUnsupportedOnce()
        {
            if (s_reportedUnsupported) return;
            s_reportedUnsupported = true;
            TestContext.Progress.WriteLine(
                "Managed allocation assertion not available: the current Unity Mono counter does not advance. " +
                "Use Unity Profiler GC.Alloc samples for release evidence.");
        }
    }
}
#endif
