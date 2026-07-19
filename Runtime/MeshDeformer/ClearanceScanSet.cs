using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum ClearanceScanRendererRole
    {
        Target = 0,
        Reference = 1
    }

    [Serializable]
    public sealed class ClearanceBlendShapeOverride
    {
        [SerializeField] private ClearanceScanRendererRole _rendererRole;
        [SerializeField] private string _blendShapeName = "";
        [SerializeField, Range(0f, 100f)] private float _weight;

        public ClearanceScanRendererRole RendererRole { get => _rendererRole; set => _rendererRole = value; }
        public string BlendShapeName { get => _blendShapeName; set => _blendShapeName = value ?? ""; }
        public float Weight { get => _weight; set => _weight = Mathf.Clamp(value, 0f, 100f); }
    }

    [Serializable]
    public sealed class ClearanceTransformPoseOverride
    {
        [SerializeField] private string _relativePath = "";
        [SerializeField] private bool _overridePosition = true;
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private bool _overrideRotation = true;
        [SerializeField] private Vector3 _localEulerAngles;
        [SerializeField] private bool _overrideScale;
        [SerializeField] private Vector3 _localScale = Vector3.one;

        public string RelativePath { get => _relativePath; set => _relativePath = value ?? ""; }
        public bool OverridePosition { get => _overridePosition; set => _overridePosition = value; }
        public Vector3 LocalPosition { get => _localPosition; set => _localPosition = value; }
        public bool OverrideRotation { get => _overrideRotation; set => _overrideRotation = value; }
        public Vector3 LocalEulerAngles { get => _localEulerAngles; set => _localEulerAngles = value; }
        public bool OverrideScale { get => _overrideScale; set => _overrideScale = value; }
        public Vector3 LocalScale { get => _localScale; set => _localScale = value; }
    }

    [Serializable]
    public sealed class ClearanceScanCondition
    {
        [SerializeField] private string _name = "Condition";
        [SerializeField] private bool _useAnimationClip;
        [SerializeField] private AnimationClip _animationClip;
        [SerializeField, Min(0f)] private float _sampleTime;
        [SerializeField] private string _animationRootPath = "";
        [SerializeField] private List<ClearanceBlendShapeOverride> _blendShapeOverrides =
            new List<ClearanceBlendShapeOverride>();
        [SerializeField] private List<ClearanceTransformPoseOverride> _transformOverrides =
            new List<ClearanceTransformPoseOverride>();
        [SerializeField] private bool _overrideThresholds;
        [SerializeField, Min(0f)] private float _warningDistance = 0.005f;
        [SerializeField, Min(0f)] private float _targetDistance = 0.01f;

        public string Name { get => _name; set => _name = value ?? ""; }
        public bool UseAnimationClip { get => _useAnimationClip; set => _useAnimationClip = value; }
        public AnimationClip AnimationClip { get => _animationClip; set => _animationClip = value; }
        public float SampleTime { get => Mathf.Max(0f, _sampleTime); set => _sampleTime = Mathf.Max(0f, value); }
        public string AnimationRootPath { get => _animationRootPath; set => _animationRootPath = value ?? ""; }
        public List<ClearanceBlendShapeOverride> BlendShapeOverrides =>
            _blendShapeOverrides ?? (_blendShapeOverrides = new List<ClearanceBlendShapeOverride>());
        public List<ClearanceTransformPoseOverride> TransformOverrides =>
            _transformOverrides ?? (_transformOverrides = new List<ClearanceTransformPoseOverride>());
        public bool OverrideThresholds { get => _overrideThresholds; set => _overrideThresholds = value; }
        public float WarningDistance { get => Mathf.Max(0f, _warningDistance); set => _warningDistance = Mathf.Max(0f, value); }
        public float TargetDistance
        {
            get => Mathf.Max(WarningDistance, _targetDistance);
            set => _targetDistance = Mathf.Max(WarningDistance, value);
        }
    }

    [CreateAssetMenu(
        fileName = "ClearanceScanSet",
        menuName = "Lattice Deformation Tool/Clearance Scan Set")]
    public sealed class ClearanceScanSet : ScriptableObject
    {
        [SerializeField] private List<ClearanceScanCondition> _conditions =
            new List<ClearanceScanCondition>();

        public List<ClearanceScanCondition> Conditions =>
            _conditions ?? (_conditions = new List<ClearanceScanCondition>());
    }
}
