#if UNITY_EDITOR
using nadena.dev.ndmf.preview;
using UnityEditor;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticePreviewUtility
    {
        /// <summary>
        /// Determines whether the runtime mesh should be assigned back to the renderer.
        /// When the NDMF preview pipeline is active we leave the original mesh untouched
        /// and rely on proxy renderers instead.
        /// </summary>
        public static bool ShouldAssignRuntimeMesh()
        {
            if (NDMFPreview.DisablePreviewDepth != 0)
            {
                return true;
            }

            if (!NDMFPreviewPrefs.instance.EnablePreview)
            {
                return true;
            }

            return !LatticeDeformerPreviewFilter.PreviewToggleEnabled;
        }

        public static void RequestSceneRepaint()
        {
            SceneView.RepaintAll();
        }
    }
}
#endif
