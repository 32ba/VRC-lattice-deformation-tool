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
            if ((outputsCandidateWeightsDirectly || !recomputeComposedSurfaceDeltas) &&
                !options.LegacyPublishedBlendShapes &&
                (options.RecalculateNormals || options.RecalculateTangents))
            {
                candidateDeltaNormals = options.RecalculateNormals ? new Vector3[candidates.Length][] : null;
                candidateDeltaTangents = options.RecalculateTangents ? new Vector3[candidates.Length][] : null;
                for (int candidate = 0; candidate < candidates.Length; candidate++)
                {
                    CalculateGeneratedSurfaceDeltasWithNormalsMode(
                        mesh,
                        baseVertices,
                        candidates[candidate],
                        options.NormalsMode,
                        options.RecalculateNormals,
                        options.RecalculateTangents,
                        out var normals,
                        out var tangents);
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
                    CalculateGeneratedSurfaceDeltasWithNormalsMode(
                        mesh,
                        baseVertices,
                        frameDeltas,
                        options.NormalsMode,
                        options.RecalculateNormals,
                        options.RecalculateTangents,
                        out frameNormals,
                        out frameTangents);
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
            deltaNormals = null;
            deltaTangents = null;

            if (template == null || baseVertices == null || deltas == null || baseVertices.Length != deltas.Length)
            {
                return;
            }

            Mesh baseMesh = null;
            Mesh targetMesh = null;
            try
            {
                baseMesh = UnityEngine.Object.Instantiate(template);
                targetMesh = UnityEngine.Object.Instantiate(template);

                int vertexCount = baseVertices.Length;
                var targetVertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    targetVertices[i] = baseVertices[i] + deltas[i];
                }

                baseMesh.vertices = baseVertices;
                targetMesh.vertices = targetVertices;

                if (includeNormals)
                {
                    RecalculateSurfaceNormals(baseMesh, template, normalsMode);
                    RecalculateSurfaceNormals(targetMesh, template, normalsMode);

                    var baseNormals = baseMesh.normals;
                    var targetNormals = targetMesh.normals;
                    if (baseNormals != null && targetNormals != null &&
                        baseNormals.Length == vertexCount && targetNormals.Length == vertexCount)
                    {
                        deltaNormals = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaNormals[i] = targetNormals[i] - baseNormals[i];
                        }
                    }
                }

                if (includeTangents)
                {
                    RecalculateSurfaceNormals(baseMesh, template, normalsMode);
                    RecalculateSurfaceNormals(targetMesh, template, normalsMode);
                    baseMesh.RecalculateTangents();
                    targetMesh.RecalculateTangents();

                    var baseTangents = baseMesh.tangents;
                    var targetTangents = targetMesh.tangents;
                    if (baseTangents != null && targetTangents != null &&
                        baseTangents.Length == vertexCount && targetTangents.Length == vertexCount)
                    {
                        deltaTangents = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            deltaTangents[i] = new Vector3(
                                targetTangents[i].x - baseTangents[i].x,
                                targetTangents[i].y - baseTangents[i].y,
                                targetTangents[i].z - baseTangents[i].z);
                        }
                    }
                }
            }
            finally
            {
                DestroyTemporaryMesh(baseMesh);
                DestroyTemporaryMesh(targetMesh);
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
            var output = UnityEngine.Object.Instantiate(input);
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
