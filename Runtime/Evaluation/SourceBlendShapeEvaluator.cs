using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    internal enum SourceBlendShapeExtrapolation { UnityFrameInterval, ClampLastFrame }

    internal static class SourceBlendShapeEvaluator
    {
        internal static Vector3[] EvaluateDelta(Mesh mesh, int shapeIndex, float weight,
            SourceBlendShapeExtrapolation extrapolation = SourceBlendShapeExtrapolation.UnityFrameInterval)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            int vertexCount = mesh.vertexCount;
            var lower = new Vector3[vertexCount];
            var upper = new Vector3[vertexCount];
            var unusedNormals = new Vector3[vertexCount];
            var unusedTangents = new Vector3[vertexCount];

            if (frameCount == 0)
            {
                return lower;
            }

            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (weight <= firstWeight || frameCount == 1)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, 0, lower, unusedNormals, unusedTangents);
                float scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                ScaleDeltas(lower, scale);
                return lower;
            }

            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight <= upperWeight)
                {
                    float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame - 1);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame - 1, lower, unusedNormals, unusedTangents);
                    mesh.GetBlendShapeFrameVertices(shapeIndex, frame, upper, unusedNormals, unusedTangents);

                    float t = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                        ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                        : 0f;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        lower[i] = Vector3.LerpUnclamped(lower[i], upper[i], t);
                    }

                    return lower;
                }
            }

            int lastFrame = frameCount - 1;
            mesh.GetBlendShapeFrameVertices(shapeIndex, lastFrame, lower, unusedNormals, unusedTangents);
            if (extrapolation == SourceBlendShapeExtrapolation.ClampLastFrame) return lower;
            float lastWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lastFrame);
            float previousWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lastFrame - 1);
            float interval = lastWeight - previousWeight;
            if (Mathf.Abs(interval) > Mathf.Epsilon)
            {
                // Unity extrapolates the last frame itself over the final frame interval;
                // it does not continue the slope between the final two delta arrays.
                float scale = 1f + (weight - lastWeight) / interval;
                ScaleDeltas(lower, scale);
            }
            return lower;
        }

        internal static void ScaleDeltas(Vector3[] deltas, float scale)
        {
            if (deltas == null)
            {
                return;
            }

            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] *= scale;
            }
        }
    }
}
