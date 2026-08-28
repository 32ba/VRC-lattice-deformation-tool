#if UNITY_EDITOR && LATTICE_MODULAR_AVATAR_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class ModularAvatarSetupOutfitWorkflowTests
    {
        private const string GeneratedAssetDirectory =
            "Packages/nadena.dev.ndmf/__Generated/MA Setup Outfit E2E Avatar";

        internal sealed class PreviewFixture : IDisposable
        {
            internal GameObject AvatarRoot { get; }
            internal GameObject OutfitRoot { get; }
            internal GameObject MeshObject { get; }
            internal SkinnedMeshRenderer Renderer { get; }
            internal LatticeDeformer Deformer { get; }
            internal Mesh SourceMesh { get; }
            internal Avatar HumanoidAvatar { get; }
            internal Component ShapeChanger { get; }
            internal Vector3 ShapeDelta { get; }
            internal Transform BaseHips { get; }
            internal Transform BaseLeftUpperArm { get; }

            internal PreviewFixture(
                GameObject avatarRoot,
                GameObject outfitRoot,
                GameObject meshObject,
                SkinnedMeshRenderer renderer,
                LatticeDeformer deformer,
                Mesh sourceMesh,
                Avatar humanoidAvatar,
                Component shapeChanger,
                Vector3 shapeDelta,
                Transform baseHips,
                Transform baseLeftUpperArm)
            {
                AvatarRoot = avatarRoot;
                OutfitRoot = outfitRoot;
                MeshObject = meshObject;
                Renderer = renderer;
                Deformer = deformer;
                SourceMesh = sourceMesh;
                HumanoidAvatar = humanoidAvatar;
                ShapeChanger = shapeChanger;
                ShapeDelta = shapeDelta;
                BaseHips = baseHips;
                BaseLeftUpperArm = baseLeftUpperArm;
            }

            public void Dispose()
            {
                if (AvatarRoot != null)
                    Object.DestroyImmediate(AvatarRoot);
                AssetDatabase.DeleteAsset(GeneratedAssetDirectory);
                if (HumanoidAvatar != null && !EditorUtility.IsPersistent(HumanoidAvatar))
                    Object.DestroyImmediate(HumanoidAvatar);
                if (SourceMesh != null && !EditorUtility.IsPersistent(SourceMesh))
                    Object.DestroyImmediate(SourceMesh);
            }
        }

        internal static PreviewFixture CreatePreviewFixture()
        {
            Type descriptorType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Type setupOutfitType = FindType("nadena.dev.modular_avatar.core.editor.SetupOutfit");
            Assert.That(descriptorType, Is.Not.Null, "The representative workflow requires the VRChat Avatars SDK.");
            Assert.That(setupOutfitType, Is.Not.Null, "Modular Avatar Setup Outfit was not found.");

            var avatarRoot = new GameObject("MA Setup Outfit Preview E2E Avatar");
            Avatar humanoidAvatar = null;
            Mesh sourceMesh = null;
            try
            {
                avatarRoot.AddComponent(descriptorType);
                Rig baseRig = CreateRig(avatarRoot.transform, "Armature", Vector3.one);
                humanoidAvatar = BuildHumanoidAvatar(avatarRoot, baseRig);
                Assert.That(humanoidAvatar, Is.Not.Null);
                Assert.That(humanoidAvatar.isValid && humanoidAvatar.isHuman, Is.True);
                avatarRoot.AddComponent<Animator>().avatar = humanoidAvatar;

                var outfitRoot = new GameObject("Representative Outfit");
                outfitRoot.transform.SetParent(avatarRoot.transform, false);
                outfitRoot.transform.localPosition = new Vector3(0.08f, -0.03f, 0.02f);
                outfitRoot.transform.localRotation = Quaternion.Euler(0f, 6f, 0f);
                Rig outfitRig = CreateRig(
                    outfitRoot.transform,
                    "OutfitArmature",
                    new Vector3(1.08f, 0.94f, 1.03f));

                var meshObject = new GameObject("Outfit Mesh");
                meshObject.transform.SetParent(outfitRoot.transform, false);
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
                sourceMesh = CreateOutfitMesh(renderer.transform, outfitRig);
                renderer.sharedMesh = sourceMesh;
                renderer.bones = new[] { outfitRig.Hips, outfitRig.LeftUpperArm };
                renderer.rootBone = outfitRig.Hips;
                renderer.localBounds = sourceMesh.bounds;

                MethodInfo setupOutfit = setupOutfitType.GetMethod(
                    "SetupOutfitUI",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(setupOutfit, Is.Not.Null);
                setupOutfit.Invoke(null, new object[] { outfitRoot });

                var deformer = meshObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                deformer.AlignMode = LatticeDeformer.LatticeAlignMode.Mode3_BoundsRemap;
                int editedControl = deformer.EditingSettings.ControlPointCount - 1;
                deformer.EditingSettings.SetControlPointLocal(
                    editedControl,
                    deformer.EditingSettings.GetControlPointLocal(editedControl) +
                    new Vector3(0.12f, 0.04f, -0.03f));
                deformer.NotifyDeformationDataChanged();

                Component shapeChanger = AddShapeChanger(outfitRoot, meshObject);
                renderer.SetBlendShapeWeight(0, 100f);

                return new PreviewFixture(
                    avatarRoot,
                    outfitRoot,
                    meshObject,
                    renderer,
                    deformer,
                    sourceMesh,
                    humanoidAvatar,
                    shapeChanger,
                    new Vector3(0f, -0.45f, 0.08f),
                    baseRig.Hips,
                    baseRig.LeftUpperArm);
            }
            catch
            {
                Object.DestroyImmediate(avatarRoot);
                if (humanoidAvatar != null && !EditorUtility.IsPersistent(humanoidAvatar))
                    Object.DestroyImmediate(humanoidAvatar);
                if (sourceMesh != null && !EditorUtility.IsPersistent(sourceMesh))
                    Object.DestroyImmediate(sourceMesh);
                throw;
            }
        }

        [Test]
        [Category("MaSetupOutfitE2E")]
        public void SetupOutfitThenDeform_RetargetsBonesWithoutChangingTheEditedMesh()
        {
            Type descriptorType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Type setupOutfitType = FindType("nadena.dev.modular_avatar.core.editor.SetupOutfit");
            Type mergeArmatureType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMergeArmature");
            Type meshSettingsType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMeshSettings");
            Assert.That(descriptorType, Is.Not.Null, "The representative workflow requires the VRChat Avatars SDK.");
            Assert.That(setupOutfitType, Is.Not.Null, "Modular Avatar Setup Outfit was not found.");
            Assert.That(mergeArmatureType, Is.Not.Null);
            Assert.That(meshSettingsType, Is.Not.Null);

            var avatarRoot = new GameObject("MA Setup Outfit E2E Avatar");
            Avatar humanoidAvatar = null;
            Mesh sourceMesh = null;
            try
            {
                avatarRoot.AddComponent(descriptorType);
                Rig baseRig = CreateRig(avatarRoot.transform, "Armature", Vector3.one);
                humanoidAvatar = BuildHumanoidAvatar(avatarRoot, baseRig);
                Assert.That(humanoidAvatar, Is.Not.Null);
                Assert.That(humanoidAvatar.isValid, Is.True);
                Assert.That(humanoidAvatar.isHuman, Is.True);
                var animator = avatarRoot.AddComponent<Animator>();
                animator.avatar = humanoidAvatar;

                var outfitRoot = new GameObject("Representative Outfit");
                outfitRoot.transform.SetParent(avatarRoot.transform, false);
                outfitRoot.transform.localPosition = new Vector3(0.08f, -0.03f, 0.02f);
                outfitRoot.transform.localRotation = Quaternion.Euler(0f, 6f, 0f);
                Rig outfitRig = CreateRig(
                    outfitRoot.transform,
                    "OutfitArmature",
                    new Vector3(1.08f, 0.94f, 1.03f));

                var meshObject = new GameObject("Outfit Mesh");
                meshObject.transform.SetParent(outfitRoot.transform, false);
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
                sourceMesh = CreateOutfitMesh(renderer.transform, outfitRig);
                renderer.sharedMesh = sourceMesh;
                renderer.bones = new[] { outfitRig.Hips, outfitRig.LeftUpperArm };
                renderer.rootBone = outfitRig.Hips;
                renderer.localBounds = sourceMesh.bounds;

                MethodInfo setupOutfit = setupOutfitType.GetMethod(
                    "SetupOutfitUI",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(setupOutfit, Is.Not.Null);
                setupOutfit.Invoke(null, new object[] { outfitRoot });

                Component mergeArmature = outfitRig.Root.GetComponent(mergeArmatureType);
                Assert.That(mergeArmature, Is.Not.Null,
                    "Setup Outfit must add MA Merge Armature to the outfit armature.");
                Assert.That(outfitRoot.GetComponent(meshSettingsType), Is.Not.Null,
                    "Setup Outfit must add MA Mesh Settings to the outfit root.");

                var deformer = meshObject.AddComponent<LatticeDeformer>();
                deformer.Reset();
                LatticeAsset settings = deformer.EditingSettings;
                Assert.That(settings, Is.Not.Null);
                int editedControl = settings.ControlPointCount - 1;
                settings.SetControlPointLocal(
                    editedControl,
                    settings.GetControlPointLocal(editedControl) + new Vector3(0.12f, 0.04f, -0.03f));
                deformer.NotifyDeformationDataChanged();

                Mesh expectedMesh = deformer.Deform(false);
                Assert.That(expectedMesh, Is.Not.Null);
                Vector3[] expectedVertices = expectedMesh.vertices;
                Assert.That(
                    expectedVertices.Zip(sourceMesh.vertices, (after, before) => Vector3.Distance(after, before))
                        .Max(),
                    Is.GreaterThan(1e-4f),
                    "The fixture must contain an actual lattice edit before MA retargeting.");

                BuildContext context = AvatarProcessor.ProcessAvatar(
                    avatarRoot,
                    nadena.dev.ndmf.platform.AmbientPlatform.CurrentPlatform);
                Assert.That(context.Successful, Is.True);
                Assert.That(renderer.sharedMesh, Is.Not.Null);
                Assert.That(renderer.sharedMesh, Is.Not.SameAs(sourceMesh));
                AssertVerticesEqual(expectedVertices, renderer.sharedMesh.vertices);
                Assert.That(renderer.bones, Does.Contain(baseRig.Hips));
                Assert.That(renderer.bones, Does.Contain(baseRig.LeftUpperArm));
                Assert.That(
                    renderer.bones.All(bone => bone != null && !ReferenceEquals(bone, outfitRig.Hips)),
                    Is.True,
                    "MA must replace every retained outfit bone reference with an avatar bone.");
                Assert.That(renderer.rootBone, Is.Not.Null);
                Assert.That(renderer.localBounds.size.sqrMagnitude, Is.GreaterThan(1e-8f));
                Assert.That(renderer.sharedMesh.bindposes, Has.Length.EqualTo(renderer.bones.Length));
                Assert.That(renderer.sharedMesh.vertices.All(IsFinite), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(avatarRoot);
                AssetDatabase.DeleteAsset(GeneratedAssetDirectory);
                if (humanoidAvatar != null && !EditorUtility.IsPersistent(humanoidAvatar))
                    Object.DestroyImmediate(humanoidAvatar);
                if (sourceMesh != null && !EditorUtility.IsPersistent(sourceMesh))
                    Object.DestroyImmediate(sourceMesh);
            }
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private static Avatar BuildHumanoidAvatar(GameObject root, Rig rig)
        {
            var mapping = new Dictionary<HumanBodyBones, Transform>
            {
                [HumanBodyBones.Hips] = rig.Hips,
                [HumanBodyBones.Spine] = rig.Spine,
                [HumanBodyBones.Chest] = rig.Chest,
                [HumanBodyBones.Neck] = rig.Neck,
                [HumanBodyBones.Head] = rig.Head,
                [HumanBodyBones.LeftUpperArm] = rig.LeftUpperArm,
                [HumanBodyBones.LeftLowerArm] = rig.LeftLowerArm,
                [HumanBodyBones.LeftHand] = rig.LeftHand,
                [HumanBodyBones.RightUpperArm] = rig.RightUpperArm,
                [HumanBodyBones.RightLowerArm] = rig.RightLowerArm,
                [HumanBodyBones.RightHand] = rig.RightHand,
                [HumanBodyBones.LeftUpperLeg] = rig.LeftUpperLeg,
                [HumanBodyBones.LeftLowerLeg] = rig.LeftLowerLeg,
                [HumanBodyBones.LeftFoot] = rig.LeftFoot,
                [HumanBodyBones.RightUpperLeg] = rig.RightUpperLeg,
                [HumanBodyBones.RightLowerLeg] = rig.RightLowerLeg,
                [HumanBodyBones.RightFoot] = rig.RightFoot,
            };
            HumanBone[] human = mapping.Select(pair => new HumanBone
            {
                boneName = pair.Value.name,
                humanName = HumanTrait.BoneName[(int)pair.Key],
                limit = new HumanLimit { useDefaultValues = true },
            }).ToArray();
            var description = new HumanDescription
            {
                human = human,
                armStretch = 0.05f,
                legStretch = 0.05f,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };
            return AvatarBuilder.BuildHumanAvatar(root, description);
        }

        private static Rig CreateRig(Transform parent, string rootName, Vector3 rootScale)
        {
            Transform root = Bone(rootName, parent, Vector3.zero);
            root.localScale = rootScale;
            Transform hips = Bone("Hips", root, new Vector3(0f, 1f, 0f));
            Transform spine = Bone("Spine", hips, new Vector3(0f, 0.2f, 0f));
            Transform chest = Bone("Chest", spine, new Vector3(0f, 0.2f, 0f));
            Transform neck = Bone("Neck", chest, new Vector3(0f, 0.18f, 0f));
            Transform head = Bone("Head", neck, new Vector3(0f, 0.16f, 0f));
            Transform leftUpperArm = Bone("LeftUpperArm", chest, new Vector3(-0.22f, 0.12f, 0f));
            Transform leftLowerArm = Bone("LeftLowerArm", leftUpperArm, new Vector3(-0.28f, 0f, 0f));
            Transform leftHand = Bone("LeftHand", leftLowerArm, new Vector3(-0.22f, 0f, 0f));
            Transform rightUpperArm = Bone("RightUpperArm", chest, new Vector3(0.22f, 0.12f, 0f));
            Transform rightLowerArm = Bone("RightLowerArm", rightUpperArm, new Vector3(0.28f, 0f, 0f));
            Transform rightHand = Bone("RightHand", rightLowerArm, new Vector3(0.22f, 0f, 0f));
            Transform leftUpperLeg = Bone("LeftUpperLeg", hips, new Vector3(-0.1f, -0.08f, 0f));
            Transform leftLowerLeg = Bone("LeftLowerLeg", leftUpperLeg, new Vector3(0f, -0.42f, 0f));
            Transform leftFoot = Bone("LeftFoot", leftLowerLeg, new Vector3(0f, -0.4f, 0.08f));
            Transform rightUpperLeg = Bone("RightUpperLeg", hips, new Vector3(0.1f, -0.08f, 0f));
            Transform rightLowerLeg = Bone("RightLowerLeg", rightUpperLeg, new Vector3(0f, -0.42f, 0f));
            Transform rightFoot = Bone("RightFoot", rightLowerLeg, new Vector3(0f, -0.4f, 0.08f));
            return new Rig(
                root, hips, spine, chest, neck, head,
                leftUpperArm, leftLowerArm, leftHand,
                rightUpperArm, rightLowerArm, rightHand,
                leftUpperLeg, leftLowerLeg, leftFoot,
                rightUpperLeg, rightLowerLeg, rightFoot);
        }

        private static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            return gameObject.transform;
        }

        private static Mesh CreateOutfitMesh(Transform rendererTransform, Rig rig)
        {
            var vertices = new[]
            {
                new Vector3(-0.62f, 1.12f, -0.08f),
                new Vector3(-0.18f, 1.12f, -0.08f),
                new Vector3(-0.62f, 1.55f, -0.08f),
                new Vector3(-0.18f, 1.55f, -0.08f),
                new Vector3(-0.62f, 1.12f, 0.08f),
                new Vector3(-0.18f, 1.12f, 0.08f),
                new Vector3(-0.62f, 1.55f, 0.08f),
                new Vector3(-0.18f, 1.55f, 0.08f),
            };
            var weights = vertices.Select(vertex => vertex.x < -0.4f
                ? new BoneWeight { boneIndex0 = 1, weight0 = 0.8f, boneIndex1 = 0, weight1 = 0.2f }
                : new BoneWeight { boneIndex0 = 0, weight0 = 0.8f, boneIndex1 = 1, weight1 = 0.2f })
                .ToArray();
            var mesh = new Mesh
            {
                name = "Representative MA Setup Outfit Mesh",
                vertices = vertices,
                triangles = new[]
                {
                    0, 2, 1, 1, 2, 3,
                    4, 5, 6, 5, 7, 6,
                    0, 4, 2, 2, 4, 6,
                    1, 3, 5, 3, 7, 5,
                },
                boneWeights = weights,
                bindposes = new[]
                {
                    rig.Hips.worldToLocalMatrix * rendererTransform.localToWorldMatrix,
                    rig.LeftUpperArm.worldToLocalMatrix * rendererTransform.localToWorldMatrix,
                },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var shapeDelta = Enumerable.Repeat(
                new Vector3(0f, -0.45f, 0.08f),
                vertices.Length).ToArray();
            mesh.AddBlendShapeFrame(
                "Representative Shape",
                100f,
                shapeDelta,
                new Vector3[vertices.Length],
                new Vector3[vertices.Length]);
            return mesh;
        }

        private static Component AddShapeChanger(GameObject host, GameObject meshObject)
        {
            Type changerType = FindType("nadena.dev.modular_avatar.core.ModularAvatarShapeChanger");
            Type changedShapeType = FindType("nadena.dev.modular_avatar.core.ChangedShape");
            Type objectReferenceType = FindType("nadena.dev.modular_avatar.core.AvatarObjectReference");
            Type changeType = FindType("nadena.dev.modular_avatar.core.ShapeChangeType");
            Assert.That(changerType, Is.Not.Null);
            Assert.That(changedShapeType, Is.Not.Null);
            Assert.That(objectReferenceType, Is.Not.Null);
            Assert.That(changeType, Is.Not.Null);

            Component changer = host.AddComponent(changerType);
            object changedShape = Activator.CreateInstance(changedShapeType);
            object objectReference = Activator.CreateInstance(objectReferenceType);
            objectReferenceType.GetMethod("Set", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(objectReference, new object[] { meshObject });
            MethodInfo getReference = objectReferenceType.GetMethod(
                "Get",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Component) },
                null);
            Assert.That(getReference?.Invoke(objectReference, new object[] { changer }), Is.SameAs(meshObject),
                "The MA Shape Changer reference must resolve to the representative outfit mesh.");
            changedShapeType.GetField("Object")?.SetValue(changedShape, objectReference);
            changedShapeType.GetField("ShapeName")?.SetValue(changedShape, "Representative Shape");
            changedShapeType.GetField("ChangeType")?.SetValue(
                changedShape,
                Enum.Parse(changeType, "Set"));
            changedShapeType.GetField("Value")?.SetValue(changedShape, 100f);
            object shapes = changerType.GetProperty("Shapes")?.GetValue(changer);
            Assert.That(shapes, Is.AssignableTo<System.Collections.IList>());
            ((System.Collections.IList)shapes).Add(changedShape);
            EditorUtility.SetDirty(changer);
            return changer;
        }

        private static void AssertVerticesEqual(Vector3[] expected, Vector3[] actual)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThanOrEqualTo(1e-5f),
                    $"MA retargeting changed the already-deformed vertex at index {i}.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private readonly struct Rig
        {
            internal readonly Transform Root;
            internal readonly Transform Hips;
            internal readonly Transform Spine;
            internal readonly Transform Chest;
            internal readonly Transform Neck;
            internal readonly Transform Head;
            internal readonly Transform LeftUpperArm;
            internal readonly Transform LeftLowerArm;
            internal readonly Transform LeftHand;
            internal readonly Transform RightUpperArm;
            internal readonly Transform RightLowerArm;
            internal readonly Transform RightHand;
            internal readonly Transform LeftUpperLeg;
            internal readonly Transform LeftLowerLeg;
            internal readonly Transform LeftFoot;
            internal readonly Transform RightUpperLeg;
            internal readonly Transform RightLowerLeg;
            internal readonly Transform RightFoot;

            internal Rig(
                Transform root, Transform hips, Transform spine, Transform chest, Transform neck, Transform head,
                Transform leftUpperArm, Transform leftLowerArm, Transform leftHand,
                Transform rightUpperArm, Transform rightLowerArm, Transform rightHand,
                Transform leftUpperLeg, Transform leftLowerLeg, Transform leftFoot,
                Transform rightUpperLeg, Transform rightLowerLeg, Transform rightFoot)
            {
                Root = root;
                Hips = hips;
                Spine = spine;
                Chest = chest;
                Neck = neck;
                Head = head;
                LeftUpperArm = leftUpperArm;
                LeftLowerArm = leftLowerArm;
                LeftHand = leftHand;
                RightUpperArm = rightUpperArm;
                RightLowerArm = rightLowerArm;
                RightHand = rightHand;
                LeftUpperLeg = leftUpperLeg;
                LeftLowerLeg = leftLowerLeg;
                LeftFoot = leftFoot;
                RightUpperLeg = rightUpperLeg;
                RightLowerLeg = rightLowerLeg;
                RightFoot = rightFoot;
            }
        }
    }
}
#endif
