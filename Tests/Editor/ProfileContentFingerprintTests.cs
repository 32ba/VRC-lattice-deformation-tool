#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ProfileContentFingerprintTests
    {
        [TestCase("_brushDisplacements")]
        [TestCase("_vertexMask")]
        [TestCase("_fitCorrectionConstraintMask")]
        [TestCase("_controlPointsLocal")]
        public void DirectArrayMutation_IsDetectedWithoutChangingSource(string field)
        {
            var layer = new LatticeLayer();
            var group = new DeformerGroup();
            group.LayersList.Add(layer);
            var groups = new List<DeformerGroup> { group };
            object owner = field == "_controlPointsLocal" ? layer.SerializedSettings : layer;
            var info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Array values = info.FieldType == typeof(Vector3[]) ? new Vector3[8] : new float[8];
            info.SetValue(owner, values);
            string initial = ProfileContentFingerprint.Capture(groups, 0);
            values.SetValue(info.FieldType == typeof(Vector3[]) ? (object)Vector3.up : .5f, 7);
            string serialized = JsonUtility.ToJson(DeformerProfilePayload.From(groups, 0));
            string changed = ProfileContentFingerprint.Capture(groups, 0);
            Assert.That(changed, Is.Not.EqualTo(initial));
            Assert.That(ProfileContentFingerprint.Capture(groups, 0), Is.EqualTo(changed));
            Assert.That(info.GetValue(owner), Is.SameAs(values));
            Assert.That(JsonUtility.ToJson(DeformerProfilePayload.From(groups, 0)), Is.EqualTo(serialized));
        }

        [Test]
        public void ScalarMetadataAndCurveChanges_AreDetected()
        {
            var layer = new LatticeLayer();
            var group = new DeformerGroup();
            group.LayersList.Add(layer);
            var groups = new List<DeformerGroup> { group };
            foreach (object owner in new object[] { group, layer, layer.SerializedSettings })
                foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (!field.IsDefined(typeof(SerializeField), false)) continue;
                    object old = field.GetValue(owner), replacement;
                    if (field.FieldType == typeof(string)) replacement = "fingerprint metadata change";
                    else if (field.FieldType == typeof(bool)) replacement = !(bool)old;
                    else if (field.FieldType == typeof(int)) replacement = (int)old + 1;
                    else if (field.FieldType == typeof(float)) replacement = (float)old + .25f;
                    else if (field.FieldType.IsEnum) replacement = Enum.ToObject(field.FieldType, Convert.ToInt32(old) + 1);
                    else if (field.FieldType == typeof(Bounds)) replacement = new Bounds(Vector3.one, Vector3.one * 3);
                    else if (field.FieldType == typeof(Vector3Int)) replacement = new Vector3Int(4, 5, 6);
                    else if (field.FieldType == typeof(AnimationCurve)) replacement = AnimationCurve.Linear(0, 1, 2, 3);
                    else continue; // Arrays are exercised in the direct-mutation cases.
                    string initial = ProfileContentFingerprint.Capture(groups, 0);
                    field.SetValue(owner, replacement);
                    Assert.That(ProfileContentFingerprint.Capture(groups, 0), Is.Not.EqualTo(initial), field.Name);
                    field.SetValue(owner, old);
                    Assert.That(ProfileContentFingerprint.Capture(groups, 0), Is.EqualTo(initial), field.Name);
                }
            string beforeCurve = ProfileContentFingerprint.Capture(groups, 0);
            group.SerializedBlendShapeCurve.MoveKey(0, new Keyframe(0, .42f));
            Assert.That(ProfileContentFingerprint.Capture(groups, 0), Is.Not.EqualTo(beforeCurve));
        }

        [Test]
        public void ObjectReferencesSelectionAndOrdering_ArePartOfIdentity()
        {
            var first = new GameObject("First reference");
            var second = new GameObject("Second reference");
            try
            {
                var layer = new LatticeLayer();
                var group = new DeformerGroup();
                group.LayersList.Add(layer);
                var other = new DeformerGroup { Name = "Other" };
                var groups = new List<DeformerGroup> { group, other };
                var reference = typeof(LatticeLayer).GetField("_fitCorrectionReferenceRenderer",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                reference.SetValue(layer, first.AddComponent<MeshRenderer>());
                string initial = ProfileContentFingerprint.Capture(groups, 0);
                reference.SetValue(layer, second.AddComponent<MeshRenderer>());
                string changed = ProfileContentFingerprint.Capture(groups, 0);
                Assert.That(changed, Is.Not.EqualTo(initial));
                Assert.That(ProfileContentFingerprint.Capture(groups, 1), Is.Not.EqualTo(changed));
                groups.Reverse();
                Assert.That(ProfileContentFingerprint.Capture(groups, 0), Is.Not.EqualTo(changed));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void VertexCountDoesNotScaleFingerprintAllocation()
        {
            var layer = new LatticeLayer();
            var group = new DeformerGroup();
            group.LayersList.Add(layer);
            var groups = new List<DeformerGroup> { group };
            long Measure(int count)
            {
                layer.EnsureBrushDisplacementCapacity(count);
                ProfileContentFingerprint.Capture(groups, 0); // Warm serializer and crypto paths.
                long before = GC.GetAllocatedBytesForCurrentThread();
                ProfileContentFingerprint.Capture(groups, 0);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long small = Measure(8), large = Measure(70000);
            Assert.That(large, Is.LessThanOrEqualTo(small + 65536), $"small={small}, large={large}");
        }
    }
}
#endif
