#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Utility for accessing NDMF preview proxy renderers.
    /// Uses the registered proxy map maintained by LatticeDeformerPreviewFilter.
    /// </summary>
    internal static class NDMFPreviewProxyUtility
    {
        /// <summary>
        /// Attempts to get the proxy renderer for an original renderer.
        /// Returns true if a proxy was found, false otherwise.
        /// </summary>
        /// <param name="original">The original renderer to look up.</param>
        /// <param name="proxy">The proxy renderer if found, null otherwise.</param>
        /// <returns>True if a proxy renderer was found.</returns>
        public static bool TryGetProxyRenderer(Renderer original, out Renderer proxy)
        {
            proxy = null;
            if (original == null)
            {
                return false;
            }

            // Use the proxy map maintained by LatticeDeformerPreviewFilter
            return LatticePreviewUtility.TryGetRegisteredProxy(original, out proxy) && proxy != null;
        }
    }
}
#endif
