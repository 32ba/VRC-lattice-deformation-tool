// Copy into Assets/Editor of a disposable VCC project without the lattice package.
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

public static class NativeLifetimeBaseline
{
    private static GameObject s_root;
    private static double s_finishAt;
    private static bool s_destroyed;

    public static void Capture()
    {
        Debug.Log("Lattice native lifetime control: no deformation or tests executed.");
    }

    public static void ExerciseModularAvatar()
    {
        Exercise(ArmatureLockMode.BaseToMerge, ArmatureLockMode.BidirectionalExact);
    }

    public static void ExerciseOneWay()
    {
        Exercise(ArmatureLockMode.BaseToMerge);
    }

    private static void Exercise(params ArmatureLockMode[] modes)
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Disposable batch project only.");
        s_root = new GameObject("Dependency-only native lifetime control");
        s_root.AddComponent<VRCAvatarDescriptor>();
        var target = new GameObject("Target");
        target.transform.SetParent(s_root.transform, false);
        new GameObject("Bone").transform.SetParent(target.transform, false);
        foreach (var mode in modes)
        {
            var source = new GameObject(mode.ToString());
            source.SetActive(false);
            source.transform.SetParent(s_root.transform, false);
            new GameObject("Bone").transform.SetParent(source.transform, false);
            var merge = source.AddComponent<ModularAvatarMergeArmature>();
            merge.mergeTarget.Set(target);
            merge.LockMode = mode;
            source.SetActive(true);
            if (merge.GetBonesMapping()?.Count != 1)
                throw new System.InvalidOperationException("Expected one matching bone per lock mode.");
            Debug.Log("Dependency-only native lifetime control: matched one bone for " + mode);
        }
        s_finishAt = EditorApplication.timeSinceStartup + 2;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < s_finishAt) return;
        if (!s_destroyed)
        {
            Object.DestroyImmediate(s_root);
            s_destroyed = true;
            s_finishAt = EditorApplication.timeSinceStartup + 1;
            return;
        }
        EditorApplication.update -= Tick;
        Debug.Log("Dependency-only native lifetime control: requested MA lock modes exercised and objects destroyed.");
        EditorApplication.Exit(0);
    }
}
