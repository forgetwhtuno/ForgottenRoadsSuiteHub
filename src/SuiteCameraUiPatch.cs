using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSuiteHub
{
    internal static class SuiteCameraCompatibility
    {
        private static readonly Dictionary<short, OpCode> Ops = BuildOps();
        internal static string LastFailure { get; private set; }

        internal static bool Verify(out MethodInfo usingUi)
        {
            usingUi = null; LastFailure = null;
            try
            {
                Type type = typeof(CameraController);
                MethodInfo candidate = Exact(type, "UsingUI", typeof(bool)), update = Exact(type, "Update", typeof(void));
                MethodInfo modern = Exact(type, "ModernControls", typeof(void)), controls = Exact(type, "Controls", typeof(void));
                if (candidate == null || update == null || modern == null || controls == null) return Fail("CameraController method shape changed", out usingUi);
                FieldInfo windows = type.GetField("UIWindows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo releaseMouse = type.GetField("releaseMouse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo dragging = typeof(GameData).GetField("DraggingUIElement", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo activeProperty = typeof(GameObject).GetProperty("activeSelf", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo activeSelf = activeProperty == null ? null : activeProperty.GetGetMethod();
                MethodInfo getAxis = typeof(Input).GetMethod("GetAxis", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                if (windows == null || windows.FieldType != typeof(List<GameObject>) || releaseMouse == null || releaseMouse.FieldType != typeof(bool) || dragging == null || dragging.FieldType != typeof(bool) || activeSelf == null || getAxis == null)
                    return Fail("CameraController member shape changed", out usingUi);
                if (!References(candidate, windows) || !References(candidate, activeSelf) || !References(update, modern) || !References(modern, candidate) || !References(modern, releaseMouse) || !References(modern, getAxis) || !References(controls, dragging))
                    return Fail("CameraController UI/control relationship changed", out usingUi);
                usingUi = candidate; return true;
            }
            catch (Exception ex) { return Fail("camera proof failed (" + ex.GetType().Name + ")", out usingUi); }
        }

        private static MethodInfo Exact(Type type, string name, Type result) { MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null); return m != null && m.ReturnType == result && m.GetParameters().Length == 0 ? m : null; }
        private static bool Fail(string text, out MethodInfo method) { method = null; LastFailure = text; return false; }
        private static bool References(MethodInfo method, MemberInfo target)
        {
            byte[] il; try { MethodBody body = method.GetMethodBody(); il = body == null ? null : body.GetILAsByteArray(); } catch { return false; }
            if (il == null) return false; int offset = 0;
            while (offset < il.Length)
            {
                OpCode op; if (!ReadOp(il, ref offset, out op)) return false; int size;
                if (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineTok || op.OperandType == OperandType.InlineType)
                {
                    if (offset + 4 > il.Length) return false; MemberInfo resolved = null;
                    try { resolved = method.Module.ResolveMember(BitConverter.ToInt32(il, offset)); } catch { }
                    try { if (resolved != null && resolved.Module == target.Module && resolved.MetadataToken == target.MetadataToken) return true; } catch { }
                    size = 4;
                }
                else if (!Size(op.OperandType, il, offset, out size)) return false;
                if (size < 0 || offset + size > il.Length) return false; offset += size;
            }
            return false;
        }
        private static bool ReadOp(byte[] il, ref int offset, out OpCode op) { op = default(OpCode); if (offset >= il.Length) return false; short value = il[offset++]; if (value == 0xFE) { if (offset >= il.Length) return false; value = (short)(0xFE00 | il[offset++]); } return Ops.TryGetValue(value, out op); }
        private static bool Size(OperandType operand, byte[] il, int offset, out int size)
        {
            size = 0; switch (operand)
            {
                case OperandType.InlineNone: return true;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; return true;
                case OperandType.InlineVar: size = 2; return true;
                case OperandType.InlineBrTarget: case OperandType.InlineI: case OperandType.InlineSig: case OperandType.InlineString: case OperandType.ShortInlineR: size = 4; return true;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; return true;
                case OperandType.InlineSwitch: if (offset + 4 > il.Length) return false; int count = BitConverter.ToInt32(il, offset); if (count < 0 || count > (il.Length - offset - 4) / 4) return false; size = 4 + count * 4; return true;
                default: return false;
            }
        }
        private static Dictionary<short, OpCode> BuildOps() { Dictionary<short, OpCode> map = new Dictionary<short, OpCode>(); FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static); for (int i = 0; i < fields.Length; i++) if (fields[i].FieldType == typeof(OpCode)) { OpCode op = (OpCode)fields[i].GetValue(null); map[op.Value] = op; } return map; }
    }

    [HarmonyPatch(typeof(CameraController), "UsingUI")]
    internal static class SuiteCameraUsingUiPatch
    {
        internal static bool ShapeVerified { get; private set; }
        [HarmonyPrepare] private static bool Prepare() { MethodInfo method; ShapeVerified = SuiteCameraCompatibility.Verify(out method); return ShapeVerified; }
        [HarmonyPostfix] private static void Postfix(ref bool __result) { try { if (!__result && SuiteDragGuard.HubOwnsDrag) __result = true; } catch { } }
    }
}
