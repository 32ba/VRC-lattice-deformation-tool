using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    // Writes caller-owned meshes; source inputs remain unchanged.
    internal static class DeformedMeshWriter
    {
        internal static void AddGeneratedBlendShapeFrames(
            Mesh mesh,
            string shapeName,
            Vector3[] baseVertices,
            GeneratedBlendShapeOutput generated, in MeshOutputOptions options, MeshOutputWorkspace workspace)
        {
            var candidates = generated.Candidates;
            if (mesh == null || string.IsNullOrEmpty(shapeName) || baseVertices == null ||
                candidates == null || candidates.Length == 0)
            {
                return;
            }

            int vertexCount = mesh.vertexCount;
            if (baseVertices.Length != vertexCount)
            {
                return;
            }
            for (int candidate = 0; candidate < candidates.Length; candidate++)
            {
                if (candidates[candidate] == null || candidates[candidate].Length != vertexCount)
                    return;
            }

            var curve = generated.Curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

            Vector3[][] candidateDeltaNormals = null;
            Vector3[][] candidateDeltaTangents = null;
            bool outputsCandidateWeightsDirectly = generated.CandidateWeights != null &&
                generated.CandidateWeights.Length == candidates.Length;
            bool recomputeComposedSurfaceDeltas = !options.LegacyPublishedBlendShapes &&
                generated.Composition != BlendShapeCompositionMode.Single &&
                (options.RecalculateNormals || options.RecalculateTangents);
            // One base surface and one reusable target mesh serve every candidate
            // and frame of this shape; see GeneratedSurfaceDeltas.
            GeneratedSurfaceDeltas surfaceDeltas = null;
            try
            {
                if ((outputsCandidateWeightsDirectly || !recomputeComposedSurfaceDeltas) &&
                    !options.LegacyPublishedBlendShapes &&
                    (options.RecalculateNormals || options.RecalculateTangents))
                {
                    candidateDeltaNormals = options.RecalculateNormals ? new Vector3[candidates.Length][] : null;
                    candidateDeltaTangents = options.RecalculateTangents ? new Vector3[candidates.Length][] : null;
                    surfaceDeltas = new GeneratedSurfaceDeltas(mesh, baseVertices, options.NormalsMode,
                        options.RecalculateNormals, options.RecalculateTangents);
                    for (int candidate = 0; candidate < candidates.Length; candidate++)
                    {
                        surfaceDeltas.Calculate(candidates[candidate], out var normals, out var tangents);
                        if (candidateDeltaNormals != null) candidateDeltaNormals[candidate] = normals;
                        if (candidateDeltaTangents != null) candidateDeltaTangents[candidate] = tangents;
                    }
                }

                if (outputsCandidateWeightsDirectly)
                {
                    for (int candidate = 0; candidate < candidates.Length; candidate++)
                    {
                        mesh.AddBlendShapeFrame(
                            shapeName,
                            generated.CandidateWeights[candidate],
                            candidates[candidate],
                            candidateDeltaNormals?[candidate],
                            candidateDeltaTangents?[candidate]);
                    }
                    return;
                }

                if (recomputeComposedSurfaceDeltas && surfaceDeltas == null)
                {
                    surfaceDeltas = new GeneratedSurfaceDeltas(mesh, baseVertices, options.NormalsMode,
                        options.RecalculateNormals, options.RecalculateTangents);
                }

                workspace.EnsureFrameCapacity(vertexCount, candidateDeltaNormals != null, candidateDeltaTangents != null);
                const int sampleCount = 100;
                for (int f = 0; f < sampleCount; f++)
                {
                    float t = (f + 1f) / sampleCount;
                    float frameWeight = t * 100f;
                    float curveValue = curve.Evaluate(t);
                    if (generated.Composition != BlendShapeCompositionMode.Single)
                    {
                        curveValue = Mathf.Clamp01(curveValue);
                    }

                    var frameDeltas = BlendShapeComposer.Compose(
                        candidates, generated.Composition, curveValue, workspace.FrameVertices);
                    Vector3[] frameNormals;
                    Vector3[] frameTangents;
                    if (recomputeComposedSurfaceDeltas)
                    {
                        surfaceDeltas.Calculate(frameDeltas, out frameNormals, out frameTangents);
                    }
                    else
                    {
                        frameNormals = candidateDeltaNormals != null
                            ? BlendShapeComposer.Compose(
                                candidateDeltaNormals, generated.Composition, curveValue, workspace.FrameNormals)
                            : null;
                        frameTangents = candidateDeltaTangents != null
                            ? BlendShapeComposer.Compose(
                                candidateDeltaTangents, generated.Composition, curveValue, workspace.FrameTangents)
                            : null;
                    }

                    mesh.AddBlendShapeFrame(shapeName, frameWeight, frameDeltas, frameNormals, frameTangents);
                }
            }
            finally
            {
                surfaceDeltas?.Dispose();
            }
        }

        internal static void CalculateGeneratedSurfaceDeltasWithNormalsMode(
            Mesh template,
            Vector3[] baseVertices,
            Vector3[] deltas,
            NormalsRecalculationMode normalsMode,
            bool includeNormals,
            bool includeTangents,
            out Vector3[] deltaNormals,
            out Vector3[] deltaTangents)
        {
            using var surfaceDeltas = new GeneratedSurfaceDeltas(
                template, baseVertices, normalsMode, includeNormals, includeTangents);
            surfaceDeltas.Calculate(deltas, out deltaNormals, out deltaTangents);
        }

        // Normal/tangent deltas of generated frames against one fixed base.
        //
        // The published rule instantiated the template twice per candidate (and
        // per recomputed frame), recalculated both surfaces and, with tangents,
        // recalculated the normals a second time. RecalculateNormals,
        // RecalculateTangents and SeamAwareMeshNormalCalculator read positions,
        // indices/topology, the just-recalculated normals and uv0 of the work
        // mesh, plus the template's positions and normals in the seam-aware
        // mode. None of them reads BlendShapes or the work mesh's previous
        // normals/tangents, and each is deterministic. Hence:
        // - the base surface depends only on (template, baseVertices) and is
        //   computed once per instance,
        // - the repeated normal recalculation reproduced the first and is skipped,
        // - one target mesh is reused: each delta overwrites its positions and
        //   recalculates every channel that is read back,
        // - work meshes are Instantiate copies of the template, so vertex and
        //   index formats (and therefore stored precision) are unchanged; their
        //   BlendShapes are cleared because nothing reads them.
        // Callers only append BlendShape frames to the template while an
        // instance is alive; its positions and normals must not change.
        private sealed class GeneratedSurfaceDeltas : IDisposable
        {
            private readonly Mesh _template;
            private readonly Vector3[] _baseVertices;
            private readonly NormalsRecalculationMode _normalsMode;
            private readonly bool _includeNormals;
            private readonly bool _includeTangents;
            private Mesh _targetMesh;
            private Vector3[] _baseNormals;
            private Vector4[] _baseTangents;
            private Vector3[] _targetVertices;
            private bool _prepared;

            internal GeneratedSurfaceDeltas(
                Mesh template,
                Vector3[] baseVertices,
                NormalsRecalculationMode normalsMode,
                bool includeNormals,
                bool includeTangents)
            {
                _template = template;
                _baseVertices = baseVertices;
                _normalsMode = normalsMode;
                _includeNormals = includeNormals;
                _includeTangents = includeTangents;
            }

            internal void Calculate(Vector3[] deltas, out Vector3[] deltaNormals, out Vector3[] deltaTangents)
            {
                deltaNormals = null;
                deltaTangents = null;

                if (_template == null || _baseVertices == null || deltas == null ||
                    _baseVertices.Length != deltas.Length)
                {
                    return;
                }

                Prepare();

                int vertexCount = _baseVertices.Length;
                if (_targetVertices == null || _targetVertices.Length != vertexCount)
                {
                    _targetVertices = new Vector3[vertexCount];
                }

                for (int i = 0; i < vertexCount; i++)
                {
                    _targetVertices[i] = _baseVertices[i] + deltas[i];
                }

                _targetMesh.vertices = _targetVertices;
                if (!_includeNormals && !_includeTangents)
                {
                    return;
                }

                RecalculateSurfaceNormals(_targetMesh, _template, _normalsMode);
                if (_includeNormals)
                {
                    var targetNormals = _targetMesh.normals;
                    if (_baseNormals != null && targetNormals != null &&
                        _baseNormals.Length == vertexCount && targetNormals.Length == vertexCount)
                    {
                        deltaNormals = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaNormals[i] = targetNormals[i] - _baseNormals[i];
                        }
                    }
                }

                if (_includeTangents)
                {
                    _targetMesh.RecalculateTangents();
                    var targetTangents = _targetMesh.tangents;
                    if (_baseTangents != null && targetTangents != null &&
                        _baseTangents.Length == vertexCount && targetTangents.Length == vertexCount)
                    {
                        deltaTangents = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaTangents[i] = new Vector3(
                                targetTangents[i].x - _baseTangents[i].x,
                                targetTangents[i].y - _baseTangents[i].y,
                                targetTangents[i].z - _baseTangents[i].z);
                        }
                    }
                }
            }

            private void Prepare()
            {
                if (_prepared)
                {
                    return;
                }

                // A Prepare that threw earlier may have left a target mesh behind.
                DestroyTemporaryMesh(_targetMesh);
                _targetMesh = null;
                Mesh baseMesh = null;
                try
                {
                    baseMesh = UnityEngine.Object.Instantiate(_template);
                    baseMesh.ClearBlendShapes();
                    _targetMesh = UnityEngine.Object.Instantiate(baseMesh);
                    baseMesh.vertices = _baseVertices;
                    if (_includeNormals || _includeTangents)
                    {
                        RecalculateSurfaceNormals(baseMesh, _template, _normalsMode);
                        if (_includeNormals)
                        {
                            _baseNormals = baseMesh.normals;
                        }

                        if (_includeTangents)
                        {
                            baseMesh.RecalculateTangents();
                            _baseTangents = baseMesh.tangents;
                        }
                    }

                    _prepared = true;
                }
                finally
                {
                    DestroyTemporaryMesh(baseMesh);
                }
            }

            public void Dispose()
            {
                DestroyTemporaryMesh(_targetMesh);
                _targetMesh = null;
            }
        }

        private static void RecalculateSurfaceNormals(
            Mesh mesh,
            Mesh sourceMesh,
            NormalsRecalculationMode normalsMode)
        {
            if (normalsMode == NormalsRecalculationMode.PreserveSourceSmoothing)
            {
                mesh.SetNormals(SeamAwareMeshNormalCalculator.Calculate(mesh, sourceMesh));
            }
            else
            {
                mesh.RecalculateNormals();
            }
        }

        internal static HashSet<string> CollectBlendShapeNames(Mesh mesh)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (mesh == null)
            {
                return names;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i));
            }

            return names;
        }

        internal static string MakeUniqueBlendShapeName(string requestedName, HashSet<string> usedNames)
        {
            usedNames ??= new HashSet<string>(StringComparer.Ordinal);

            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "BlendShape" : requestedName.Trim();
            string name = baseName;
            int suffix = 1;
            while (usedNames.Contains(name))
            {
                name = $"{baseName} {suffix}";
                suffix++;
            }

            usedNames.Add(name);
            return name;
        }

        internal static void DestroyTemporaryMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            // The release gate is EditMode-only; PlayMode destruction is a Unity branch.
#line hidden
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
#line default
        }

        internal static void CopyBlendShapes(
            Mesh source,
            Mesh destination,
            MeshOutputWorkspace workspace,
            Vector3[][] bakedBlendShapeDeltas = null,
            float[] bakedBlendShapeWeights = null)
        {
            if (source == destination) throw new ArgumentException("Source and destination meshes must be distinct.");
            int shapeCount = source.blendShapeCount;
            int vertexCount = source.vertexCount;
            if (shapeCount == 0) return;
            workspace.EnsureFrameCapacity(vertexCount, true, true);
            for (int s = 0; s < shapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frameCount = source.GetBlendShapeFrameCount(s);
                var baked = bakedBlendShapeDeltas != null && s < bakedBlendShapeDeltas.Length
                    ? bakedBlendShapeDeltas[s]
                    : null;
                float bakedWeight = bakedBlendShapeWeights != null && s < bakedBlendShapeWeights.Length
                    ? bakedBlendShapeWeights[s]
                    : 0f;
                bool hasBakedShape = baked != null && baked.Length == vertexCount;

                if (hasBakedShape && frameCount > 0)
                {
                    float firstWeight = source.GetBlendShapeFrameWeight(s, 0);
                    if (bakedWeight < firstWeight - 1e-5f)
                    {
                        workspace.ClearFrameBuffers();
                        destination.AddBlendShapeFrame(name, bakedWeight,
                            workspace.FrameVertices, workspace.FrameNormals, workspace.FrameTangents);
                    }
                }

                for (int f = 0; f < frameCount; f++)
                {
                    float weight = source.GetBlendShapeFrameWeight(s, f);
                    workspace.ClearFrameBuffers();
                    var dv = workspace.FrameVertices;
                    var dn = workspace.FrameNormals;
                    var dt = workspace.FrameTangents;
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    if (hasBakedShape)
                    {
                        for (int v = 0; v < vertexCount; v++)
                        {
                            dv[v] -= baked[v];
                        }
                    }

                    destination.AddBlendShapeFrame(name, weight, dv, dn, dt);
                }
            }
        }

        internal static void RestoreSourceNormals(Mesh source, Mesh mesh, MeshOutputWorkspace workspace)
        {
            if (mesh == null || source == null)
            {
                return;
            }

            source.GetNormals(workspace.SourceNormals);
            if (workspace.SourceNormals.Count == mesh.vertexCount)
            {
                mesh.SetNormals(workspace.SourceNormals);
            }
            else
            {
                mesh.normals = Array.Empty<Vector3>();
            }
        }

        internal static void RestoreSourceTangents(Mesh source, Mesh mesh, MeshOutputWorkspace workspace)
        {
            if (mesh == null || source == null)
            {
                return;
            }

            source.GetTangents(workspace.SourceTangents);
            if (workspace.SourceTangents.Count == mesh.vertexCount)
            {
                mesh.SetTangents(workspace.SourceTangents);
            }
            else
            {
                mesh.tangents = Array.Empty<Vector4>();
            }
        }

        internal static Mesh CloneInput(Mesh input, Vector3[] vertices)
        {
            // Unity's Mesh Instantiate marks the borrowed input dirty, which
            // invalidates source-to-bone bindings on every weight refresh.
            var output = SourceMeshAccess.CreateReadableCopy(input, copyBlendShapes: false);
            try
            {
                output.name = input.name + " (Lattice Preview)";
                output.vertices = vertices;
                output.ClearBlendShapes();
                return output;
            }
            catch
            {
                DestroyTemporaryMesh(output);
                throw;
            }
        }

        internal static void FinalizeSurface(Mesh source, Mesh output, in MeshOutputOptions options,
            MeshOutputWorkspace workspace, NormalsRecalculationMode finalNormalsMode)
        {
            if (source == output) throw new ArgumentException("Source and destination meshes must be distinct.");
            if (options.RecalculateNormals) RecalculateSurfaceNormals(output, source, finalNormalsMode);
            else RestoreSourceNormals(source, output, workspace);
            if (options.RecalculateTangents) output.RecalculateTangents();
            else RestoreSourceTangents(source, output, workspace);
            if (options.RecalculateBounds) output.RecalculateBounds();
            else output.bounds = source.bounds;
            output.UploadMeshData(false);
        }
    }
}
