#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ToolVertexVisualizationTests
    {
        private const string Fixtures = "Packages/net.32ba.lattice-deformation-tool/Tests/Editor/Fixtures/ToolVisualizationBaseline/";

        [Serializable] private sealed class StyleBaseline
        {
            public string baselineCommit;
            public ColorSample[] colors;
            public string textureRgbaBase64;
            public string shader;
            public int zWrite, srcBlend, dstBlend, cull;
        }

        [Serializable] private sealed class ColorSample
        {
            public float value;
            public float[] brush, vertex;
        }

        [Test]
        public void Gradients_MatchIndependentPublishedSamplesIncludingOutsideRange()
        {
            var baseline = ReadStyles();
            Assert.That(baseline.baselineCommit, Is.EqualTo("691723bbf64832db5ef6420cb9b89e99f3a34542"));
            Assert.That(baseline.colors, Has.Length.EqualTo(11));
            foreach (var sample in baseline.colors)
            {
                AssertColor(BrushVertexVisualization.HeatmapColor(sample.value), sample.brush);
                AssertColor(SelectedVertexVisualization.InfluenceToColor(sample.value), sample.vertex);
            }
        }

        [Test]
        public void MaterialAndTexture_MatchPublishedSettingsAndAllPixels()
        {
            using var dots = new SceneVertexDots();
            var baseline = ReadStyles();
            var material = dots.Prepare(CompareFunction.Always);
            Assert.That(material.shader.name, Is.EqualTo(baseline.shader));
            Assert.That(material.GetInt("_ZWrite"), Is.EqualTo(baseline.zWrite));
            Assert.That(material.GetInt("_SrcBlend"), Is.EqualTo(baseline.srcBlend));
            Assert.That(material.GetInt("_DstBlend"), Is.EqualTo(baseline.dstBlend));
            Assert.That(material.GetInt("_Cull"), Is.EqualTo(baseline.cull));
            Assert.That(dots.Texture.width, Is.EqualTo(32));
            Assert.That(dots.Texture.height, Is.EqualTo(32));
            Assert.That(dots.Texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(dots.Texture.GetRawTextureData<byte>().ToArray(),
                Is.EqualTo(Convert.FromBase64String(baseline.textureRgbaBase64)));
        }

        [Test]
        public void AlternatingDisplays_ResetDepthAndReleaseOnlyTheirOwnedResources()
        {
            using var first = new SceneVertexDots();
            using var second = new SceneVertexDots();
            var material = first.Prepare(CompareFunction.LessEqual);
            var texture = first.Texture;
            var otherMaterial = second.Prepare(CompareFunction.Always);
            var otherTexture = second.Texture;
            Assert.That(first.Prepare(CompareFunction.Always), Is.SameAs(material));
            Assert.That(material.GetInt("_ZTest"), Is.EqualTo((int)CompareFunction.Always));
            Assert.That(first.Prepare(CompareFunction.LessEqual), Is.SameAs(material));
            Assert.That(material.GetInt("_ZTest"), Is.EqualTo((int)CompareFunction.LessEqual));
            first.Dispose();
            first.Dispose();
            Assert.That(material == null && texture == null, Is.True);
            Assert.That(otherMaterial != null && otherTexture != null, Is.True);
            Assert.That(first.Prepare(CompareFunction.Always) != null, Is.True);
            Assert.That(first.Texture != null, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Geometry_UsesPosedWorldPositionsOrTransformedDisplacementsWithoutWriting(bool posed)
        {
            var local = new[] { new Vector3(1, 2, 3), new Vector3(-1, 0, 2) };
            var delta = new[] { Vector3.up, Vector3.right };
            var world = posed ? new[] { new Vector3(20, 30, 40), new Vector3(50, 60, 70) } : null;
            var oldLocal = local.ToArray();
            var oldDelta = delta.ToArray();
            var oldWorld = world?.ToArray();
            var matrix = Matrix4x4.TRS(new Vector3(4, 5, 6), Quaternion.Euler(10, 20, 30), new Vector3(2, 3, 4));
            var geometry = new VertexDisplayGeometry(local, world, delta, matrix);
            for (int i = 0; i < 2; i++)
                Assert.That(Vector3.Distance(geometry.WorldPosition(i), posed ? world[i] : matrix.MultiplyPoint3x4(local[i] + delta[i])), Is.LessThan(1e-5f));
            Assert.That(local, Is.EqualTo(oldLocal));
            Assert.That(delta, Is.EqualTo(oldDelta));
            Assert.That(world, Is.EqualTo(oldWorld));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("GraphicsE2E")]
        public void DotBatch_MatchesPublishedPixelsAndRecoversAfterException(bool interrupt)
        {
            using var dots = new SceneVertexDots();
            var actual = Render(() =>
            {
                if (interrupt)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        using var interrupted = dots.Begin(CompareFunction.Always);
                        throw new InvalidOperationException("Interrupted visualization");
                    });
                }
                using var batch = dots.Begin(CompareFunction.Always);
                for (int i = 0; i < 9; i++)
                {
                    var color = BrushVertexVisualization.HeatmapColor(i / 8f);
                    color.a = 0.4f + i / 20f;
                    batch.Draw(Positions()[i], color, 4f + i, Vector3.right, Vector3.up);
                }
            });
            AssertPixels(actual, "dots-baseline");
        }

        [TestCase("DrawAffectedVertices")]
        [TestCase("DrawDisplacementHeatmap")]
        [TestCase("DrawVertexMaskVisualization")]
        [Category("GraphicsE2E")]
        public void BrushDisplays_MatchPixelsFromPublishedHandler(string operation)
        {
            var local = Positions();
            var delta = Enumerable.Range(0, 9).Select(i => new Vector3(i, 0, 0)).ToArray();
            var world = local.Select((v, i) => v + delta[i]).ToArray();
            var mask = Enumerable.Range(0, 9).Select(i => i / 8f).ToArray();
            var geometry = new VertexDisplayGeometry(local, world, delta, Matrix4x4.Translate(new Vector3(0, 0, 10000)));
            var originals = new[] { local.ToArray(), world.ToArray(), delta.ToArray() };
            const float baselineHandleSize = 1960.929f;
            var actual = Render(() =>
            {
                switch (operation)
                {
                    case "DrawAffectedVertices":
                        BrushVertexVisualization.DrawAffected(geometry, new Vector3(64, 64, 0), 60f,
                            BrushFalloffType.Smooth, 1f, baselineHandleSize * 0.004f, null, null);
                        break;
                    case "DrawDisplacementHeatmap":
                        BrushVertexVisualization.DrawDisplacements(geometry, baselineHandleSize * 0.003f);
                        break;
                    default:
                        BrushVertexVisualization.DrawMask(geometry, mask, baselineHandleSize * 0.004f);
                        break;
                }
            });
            AssertPixels(actual, operation);
            Assert.That(local, Is.EqualTo(originals[0]));
            Assert.That(world, Is.EqualTo(originals[1]));
            Assert.That(delta, Is.EqualTo(originals[2]));
            Assert.That(mask, Is.EqualTo(Enumerable.Range(0, 9).Select(i => i / 8f).ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("GraphicsE2E")]
        public void SelectedVertices_MatchPublishedSelectionAndInfluencePixels(bool showInfluence)
        {
            var positions = Positions();
            var selected = new HashSet<int> { 0, 4, 8 };
            var cache = new VertexProportionalInfluenceCache();
            cache.Rebuild(positions, selected, 65f, VertexSelectionHandler.FalloffType.Smooth);
            var actual = Render(() => SelectedVertexVisualization.Draw(
                new VertexDisplayGeometry(positions, null, null, Matrix4x4.identity), selected,
                cache, showInfluence, 10f, 1f, Vector3.right, Vector3.up, CompareFunction.Always));
            AssertPixels(actual, showInfluence ? "selected-influence" : "selected-only");
            Assert.That(positions, Is.EqualTo(Positions()));
            Assert.That(selected, Is.EquivalentTo(new[] { 0, 4, 8 }));
        }

        private static Vector3[] Positions() => Enumerable.Range(0, 9)
            .Select(i => new Vector3(20 + i % 3 * 40, 20 + i / 3 * 40, 0)).ToArray();

        private static StyleBaseline ReadStyles() => JsonUtility.FromJson<StyleBaseline>(
            File.ReadAllText(Fixtures + "styles-baseline.json"));

        private static void AssertColor(Color actual, float[] expected) =>
            Assert.That(new[] { actual.r, actual.g, actual.b, actual.a }, Is.EqualTo(expected).Within(1e-6f));

        private static void AssertPixels(byte[] actual, string baseline)
        {
            var expected = File.ReadAllBytes(Fixtures + baseline + ".rgba");
            Assert.That(expected, Has.Length.EqualTo(128 * 128 * 4));
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            // Color quantization may differ by one byte on another supported graphics backend.
            int maxDifference = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
            Assert.That(maxDifference, Is.LessThanOrEqualTo(1), baseline);
            Assert.That(Enumerable.Range(0, actual.Length / 4).Count(i => actual[i * 4] + actual[i * 4 + 1] + actual[i * 4 + 2] > 0), Is.GreaterThan(100));
        }

        private static byte[] Render(Action draw)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics Editor.");
            var previous = RenderTexture.active;
            var previousCamera = Camera.current;
            var cameraObject = new GameObject("__ToolVisualizationCamera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                Camera.SetupCurrent(camera);
                target.Create();
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, 128, 0, 128);
                    draw();
                }
                finally { GL.PopMatrix(); }
                texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                texture.Apply();
                return texture.GetRawTextureData<byte>().ToArray();
            }
            finally
            {
                Camera.SetupCurrent(previousCamera);
                RenderTexture.active = previous;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(cameraObject);
                SceneVertexDots.Shared.Dispose();
            }
        }
    }
}
#endif
