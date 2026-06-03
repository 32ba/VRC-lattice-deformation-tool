#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine.Networking;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class VpmApiClient
    {
        private const string ApiBaseUrl = "https://vpm.32ba.net/api/packages";
        private const int RequestTimeoutSeconds = 10;

        private readonly string _packageId;

        internal VpmApiClient(string packageId)
        {
            _packageId = packageId;
        }

        [ExcludeFromCodeCoverage]
        internal IEnumerator GetLatestVersionCoroutine(Action<string> onComplete, Action<string> onError = null)
        {
            string url = $"{ApiBaseUrl}/{_packageId}/latest/version";

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(request.downloadHandler.text.Trim());
                }
                else
                {
                    onError?.Invoke(BuildErrorMessage(request, url));
                }
            }
        }

        internal static string BuildErrorMessage(UnityWebRequest request, string url)
        {
            string responseText = request.downloadHandler != null ? request.downloadHandler.text : null;
            return BuildErrorMessage(request, url, responseText);
        }

        internal static string BuildErrorMessage(UnityWebRequest request, string url, string responseText)
        {
            return BuildErrorMessage(request.result, request.error, request.responseCode, url, responseText);
        }

        internal static string BuildErrorMessage(
            UnityWebRequest.Result result,
            string error,
            long responseCode,
            string url,
            string responseText)
        {
            var parts = new StringBuilder();
            parts.Append("VPM API request failed");
            parts.Append($" ({result})");

            if (!string.IsNullOrEmpty(error))
            {
                parts.Append($": {error}");
            }

            if (responseCode > 0)
            {
                parts.Append($" [HTTP {responseCode}]");
            }

            parts.Append($" URL={url}");

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                responseText = responseText.Trim();
                if (responseText.Length > 200)
                {
                    responseText = responseText.Substring(0, 200) + "...";
                }

                parts.Append($" Response={responseText}");
            }

            return parts.ToString();
        }
    }
}
#endif
