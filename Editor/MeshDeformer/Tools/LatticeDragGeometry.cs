#if UNITY_EDITOR
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Borrowed pose for one synchronous drag update; never edits source data.
    internal readonly struct LatticeDragGeometry
    {
        private readonly Matrix4x4 _worldToProxy, _proxyToSource;
        private readonly Bounds _sourceBounds, _proxyBounds, _displayBounds;
        private readonly Vector3 _scale, _centerOffset, _rootOffset;
        private readonly bool _useProxy, _mapBounds, _fallback, _normalize;
        private readonly LatticeControlPointSkinning _skinning;

        internal LatticeDragGeometry(Matrix4x4 worldToProxy, Matrix4x4 proxyToSource,
            Bounds sourceBounds, Bounds proxyBounds, Vector3 scale, Vector3 centerOffset,
            Vector3 rootOffset, bool useProxy, bool mapBounds, bool fallback,
            LatticeControlPointSkinning skinning, bool normalize, Bounds displayBounds)
        {
            _worldToProxy = worldToProxy; _proxyToSource = proxyToSource;
            _sourceBounds = sourceBounds; _proxyBounds = proxyBounds;
            _scale = scale; _centerOffset = centerOffset; _rootOffset = rootOffset;
            _useProxy = useProxy; _mapBounds = mapBounds; _fallback = fallback;
            _skinning = skinning; _normalize = normalize; _displayBounds = displayBounds;
        }

        internal Vector3 StorePoint(int index, Vector3 worldPoint)
        {
            Vector3 point = _worldToProxy.MultiplyPoint3x4(worldPoint);
            point = new Vector3(_scale.x != 0f ? point.x / _scale.x : point.x,
                _scale.y != 0f ? point.y / _scale.y : point.y,
                _scale.z != 0f ? point.z / _scale.z : point.z);
            if (_useProxy)
            {
                point -= _centerOffset;
                if (_mapBounds)
                {
                    if (_fallback) point = _proxyToSource.MultiplyPoint3x4(point);
                    point = LatticeCageGeometry.MapPointBetweenBounds(point, _proxyBounds, _sourceBounds);
                }
                point -= _rootOffset;
                if (!_mapBounds) point = _proxyToSource.MultiplyPoint3x4(point);
            }
            else
            {
                point = _mapBounds
                    ? LatticeCageGeometry.MapPointBetweenBounds(point, _proxyBounds, _sourceBounds)
                    : _proxyToSource.MultiplyPoint3x4(point);
            }
            if (_skinning != null)
            {
                if (_normalize) point = LatticeCageGeometry.MapPointBetweenBounds(
                    point, _displayBounds, _skinning.PosedControlBounds);
                _skinning.TryInverseTransformPoint(index, point, out point);
            }
            return point;
        }

        internal Vector3 StoreMirrorDelta(int index, Vector3 worldDelta)
        {
            Vector3 delta = _worldToProxy.MultiplyVector(worldDelta);
            if (_useProxy && _mapBounds && _fallback)
                delta = _proxyToSource.MultiplyVector(delta);
            delta = _mapBounds
                ? LatticeCageGeometry.MapDeltaBetweenBounds(delta, _proxyBounds, _sourceBounds)
                : _proxyToSource.MultiplyVector(delta);
            // The published mirror-delta path does not divide by manual scale.
            if (_skinning != null)
            {
                if (_normalize) delta = LatticeCageGeometry.MapDeltaBetweenBounds(
                    delta, _displayBounds, _skinning.PosedControlBounds);
                _skinning.TryInverseTransformVector(index, delta, out delta);
            }
            return delta;
        }
    }
}
#endif
