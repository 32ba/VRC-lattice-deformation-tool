#if UNITY_EDITOR
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using System;
using System.Reflection;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class VRChatWhitelistTests
    {
        [Test]
        public void LatticeDeformerWhitelist_AppendComponentType_AddsMissingType()
        {
            var result = AppendComponentType(new[] { "UnityEngine.Transform" });

            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0], Is.EqualTo("UnityEngine.Transform"));
            Assert.That(result[1], Is.EqualTo("Net._32Ba.LatticeDeformationTool.LatticeDeformer"));
        }

        [Test]
        public void LatticeDeformerWhitelist_AppendComponentType_DoesNotDuplicate()
        {
            var entries = new[]
            {
                "UnityEngine.Transform",
                "Net._32Ba.LatticeDeformationTool.LatticeDeformer"
            };

            var result = AppendComponentType(entries);

            Assert.That(result, Is.SameAs(entries));
        }

        [Test]
        public void LatticeDeformerWhitelist_AppendComponentType_PreservesNull()
        {
            Assert.That(AppendComponentType(null), Is.Null);
        }

        private static string[] AppendComponentType(string[] entries)
        {
            var type = Type.GetType(
                "Net._32Ba.LatticeDeformationTool.Editor.LatticeDeformerWhitelist, net.32ba.lattice-deformation-tool.vrchat",
                throwOnError: true);
            var method = type.GetMethod("AppendComponentType", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (string[])method.Invoke(null, new object[] { entries });
        }
    }
}
#endif
