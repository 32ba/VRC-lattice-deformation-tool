#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class GuidedInspectorTests
    {
        [Test]
        public void EnsureGuidedLayer_ActiveMatchingLayerIsReused()
        {
            var fixture = CreateFixture("GuidedActiveMatching");
            try
            {
                int countBefore = fixture.Deformer.Layers.Count;

                int selected = LatticeDeformerEditor.EnsureGuidedLayer(
                    fixture.Deformer,
                    MeshDeformerLayerType.Lattice);

                Assert.That(selected, Is.EqualTo(fixture.Deformer.ActiveLayerIndex));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(countBefore));
                Assert.That(fixture.Deformer.Layers[selected].Type, Is.EqualTo(MeshDeformerLayerType.Lattice));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void EnsureGuidedLayer_ExistingMatchingLayerIsSelectedWithoutAdding()
        {
            var fixture = CreateFixture("GuidedExistingMatching");
            try
            {
                int brushIndex = fixture.Deformer.AddLayer("Existing Brush", MeshDeformerLayerType.Brush);
                fixture.Deformer.AddLayer("Active Lattice", MeshDeformerLayerType.Lattice);
                int countBefore = fixture.Deformer.Layers.Count;

                int selected = LatticeDeformerEditor.EnsureGuidedLayer(
                    fixture.Deformer,
                    MeshDeformerLayerType.Brush);

                Assert.That(selected, Is.EqualTo(brushIndex));
                Assert.That(fixture.Deformer.ActiveLayerIndex, Is.EqualTo(brushIndex));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(countBefore));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void EnsureGuidedLayer_MissingTypeCreatesAndSelectsNeutralLayer()
        {
            var fixture = CreateFixture("GuidedCreatesMissing");
            try
            {
                int countBefore = fixture.Deformer.Layers.Count;

                int selected = LatticeDeformerEditor.EnsureGuidedLayer(
                    fixture.Deformer,
                    MeshDeformerLayerType.Brush);

                Assert.That(selected, Is.GreaterThanOrEqualTo(0));
                Assert.That(fixture.Deformer.ActiveLayerIndex, Is.EqualTo(selected));
                Assert.That(fixture.Deformer.Layers.Count, Is.EqualTo(countBefore + 1));
                Assert.That(fixture.Deformer.Layers[selected].Type, Is.EqualTo(MeshDeformerLayerType.Brush));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void UseSimpleOverlay_LatticeResetsHiddenEditingOptions()
        {
            var fixture = CreateFixture("GuidedSimpleLattice");
            try
            {
                LatticeToolHandler.IncludeInteriorControls = true;
                LatticeToolHandler.ShowIndices = true;
                LatticeToolHandler.OccludeWithSceneGeometry = true;
                LatticeToolHandler.MirrorEditing = true;

                MeshDeformerTool.UseSimpleOverlay(fixture.Deformer);

                Assert.That(MeshDeformerTool.SimpleOverlayEnabled, Is.True);
                Assert.That(LatticeToolHandler.IncludeInteriorControls, Is.False);
                Assert.That(LatticeToolHandler.ShowIndices, Is.False);
                Assert.That(LatticeToolHandler.OccludeWithSceneGeometry, Is.False);
                Assert.That(LatticeToolHandler.MirrorEditing, Is.False);
            }
            finally
            {
                MeshDeformerTool.UseDetailedOverlay();
                fixture.Dispose();
            }
        }

        [Test]
        public void UseSimpleOverlay_BrushResetsHiddenEditingOptions()
        {
            var fixture = CreateFixture("GuidedSimpleBrush");
            try
            {
                int brushIndex = fixture.Deformer.AddLayer("Guided Brush", MeshDeformerLayerType.Brush);
                fixture.Deformer.ActiveLayerIndex = brushIndex;
                MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.Brush;
                BrushToolHandler.CurrentBrushMode = BrushToolHandler.BrushMode.Smooth;
                BrushToolHandler.InvertBrush = true;
                BrushToolHandler.MirrorEditing = true;
                BrushToolHandler.ConnectedOnly = true;
                BrushToolHandler.UseSurfaceDistance = true;
                BrushToolHandler.BackfaceCulling = false;

                MeshDeformerTool.UseSimpleOverlay(fixture.Deformer);

                Assert.That(BrushToolHandler.CurrentBrushMode, Is.EqualTo(BrushToolHandler.BrushMode.Normal));
                Assert.That(BrushToolHandler.InvertBrush, Is.False);
                Assert.That(BrushToolHandler.MirrorEditing, Is.False);
                Assert.That(BrushToolHandler.ConnectedOnly, Is.False);
                Assert.That(BrushToolHandler.UseSurfaceDistance, Is.False);
                Assert.That(BrushToolHandler.BackfaceCulling, Is.True);
            }
            finally
            {
                MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.Brush;
                MeshDeformerTool.UseDetailedOverlay();
                fixture.Dispose();
            }
        }

        [Test]
        public void UseSimpleOverlay_VertexSelectionResetsHiddenEditingOptions()
        {
            var fixture = CreateFixture("GuidedSimpleVertexSelection");
            try
            {
                int brushIndex = fixture.Deformer.AddLayer("Guided Vertex", MeshDeformerLayerType.Brush);
                fixture.Deformer.ActiveLayerIndex = brushIndex;
                MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.VertexSelection;
                VertexSelectionHandler.CurrentTransformMode = VertexSelectionHandler.TransformMode.Scale;
                VertexSelectionHandler.CurrentHandleOrientation = VertexSelectionHandler.HandleOrientation.Global;
                VertexSelectionHandler.CurrentPivotMode = VertexSelectionHandler.PivotMode.LastSelected;
                VertexSelectionHandler.ProportionalRadius = 0.25f;
                VertexSelectionHandler.BackfaceCulling = true;

                MeshDeformerTool.UseSimpleOverlay(fixture.Deformer);

                Assert.That(VertexSelectionHandler.CurrentTransformMode, Is.EqualTo(VertexSelectionHandler.TransformMode.Move));
                Assert.That(VertexSelectionHandler.CurrentHandleOrientation, Is.EqualTo(VertexSelectionHandler.HandleOrientation.Normal));
                Assert.That(VertexSelectionHandler.CurrentPivotMode, Is.EqualTo(VertexSelectionHandler.PivotMode.Center));
                Assert.That(VertexSelectionHandler.ProportionalRadius, Is.Zero);
                Assert.That(VertexSelectionHandler.BackfaceCulling, Is.False);
            }
            finally
            {
                MeshDeformerTool.CurrentBrushSubMode = MeshDeformerTool.BrushSubMode.Brush;
                MeshDeformerTool.UseDetailedOverlay();
                fixture.Dispose();
            }
        }

        private static Fixture CreateFixture(string name)
        {
            var gameObject = new GameObject(name);
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = TestMeshFactory.CreateCylinder(8, 3);
            gameObject.AddComponent<MeshRenderer>();
            var deformer = gameObject.AddComponent<LatticeDeformer>();
            return new Fixture(gameObject, meshFilter.sharedMesh, deformer);
        }

        private sealed class Fixture
        {
            private readonly GameObject _gameObject;
            private readonly Mesh _mesh;

            internal Fixture(GameObject gameObject, Mesh mesh, LatticeDeformer deformer)
            {
                _gameObject = gameObject;
                _mesh = mesh;
                Deformer = deformer;
            }

            internal LatticeDeformer Deformer { get; }

            internal void Dispose()
            {
                Object.DestroyImmediate(_gameObject);
                Object.DestroyImmediate(_mesh);
            }
        }
    }
}
#endif
