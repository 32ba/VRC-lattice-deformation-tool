#if UNITY_EDITOR
using System;
using UnityEditor.Callbacks;
using UnityEngine;
internal static class LatticeOfflineSdkConfig
{
    [DidReloadScripts(int.MaxValue)]
    private static void Configure()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-latticeOfflineSdkConfig") < 0) return;
        // Run after SDK initialization. Its Editor-only testing seam provides
        // local defaults; these geometry tests do not test the live service.
        VRC.Core.ConfigManager.AssignTestRemoteConfig(null);
        if (!VRC.Core.ConfigManager.RemoteConfig.IsInitialized())
            throw new InvalidOperationException("The SDK test configuration did not initialize.");
        Debug.Log("LATTICE_OFFLINE_SDK_CONFIG initialized without a remote request");
    }
}
#endif
