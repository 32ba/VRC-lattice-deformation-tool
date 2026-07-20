#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class LegacyBrushDeformerDeprecationTests
    {
        private static readonly Dictionary<short, OpCode> s_opCodes = BuildOpCodeMap();

        [Test]
        public void LegacyComponent_IsHiddenFromAddComponentMenu()
        {
            var attribute = typeof(BrushDeformer).GetCustomAttribute<AddComponentMenu>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.componentMenu, Is.Empty);
        }

        [Test]
        public void LegacyInspector_DoesNotActivateMeshDeformerTool()
        {
            var inspector = typeof(BrushDeformerEditor).GetMethod(
                nameof(BrushDeformerEditor.OnInspectorGUI),
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(inspector, Is.Not.Null);
            Assert.That(
                CallsMethod(inspector, typeof(ToolManager), "SetActiveTool"),
                Is.False,
                "The legacy Inspector must not activate a component tool that only targets LatticeDeformer.");
        }

        [Test]
        public void LegacyDeprecationWarning_IsTranslatedInEveryLanguage()
        {
            var previousLanguage = LatticeLocalization.CurrentLanguage;
            try
            {
                foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
                {
                    LatticeLocalization.CurrentLanguage = language;
                    string warning = LatticeLocalization.Tr(LocKey.LegacyBrushDeprecatedWarning);

                    Assert.That(warning, Is.Not.EqualTo(LocKey.LegacyBrushDeprecatedWarning), language.ToString());
                    Assert.That(warning, Does.Contain("Mesh Deformer"), language.ToString());
                }
            }
            finally
            {
                LatticeLocalization.CurrentLanguage = previousLanguage;
            }
        }

        private static bool CallsMethod(MethodInfo source, Type declaringType, string methodName)
        {
            byte[] il = source.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;

            int offset = 0;
            while (offset < il.Length)
            {
                OpCode opCode = ReadOpCode(il, ref offset);
                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, offset);
                    try
                    {
                        MethodBase called = source.Module.ResolveMethod(
                            token,
                            source.DeclaringType?.GetGenericArguments(),
                            source.GetGenericArguments());
                        if (called?.DeclaringType == declaringType && called.Name == methodName)
                        {
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // An unresolved metadata token is unrelated to the method under test.
                    }
                }

                offset += OperandSize(opCode.OperandType, il, offset);
            }

            return false;
        }

        private static OpCode ReadOpCode(byte[] il, ref int offset)
        {
            short value = il[offset++];
            if (value == 0xfe)
            {
                value = (short)(0xfe00 | il[offset++]);
            }

            return s_opCodes[value];
        }

        private static int OperandSize(OperandType operandType, byte[] il, int offset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return 4 + BitConverter.ToInt32(il, offset) * 4;
                default:
                    return 4;
            }
        }

        private static Dictionary<short, OpCode> BuildOpCodeMap()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode opCode)
                {
                    result[opCode.Value] = opCode;
                }
            }

            return result;
        }
    }
}
#endif
