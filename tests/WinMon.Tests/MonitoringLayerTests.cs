using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

/// <summary>
/// Monitoring is a leaf: it may use the BCL, System.Management, P/Invoke and the string resources, but nothing from
/// WPF, Windows Forms or the rest of WinMon. Reads compiled signatures and method bodies, so fully qualified names
/// and global usings cannot hide a reference.
/// </summary>
[TestFixture]
public sealed class MonitoringLayerTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] UiAssemblies =
        ["WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml", "System.Windows.Forms", "UIAutomation"];

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    [Test]
    public void Monitoring_ReferencesNoUiOrOtherWinMonNamespace()
    {
        var assembly = typeof(ProcessRanking).Assembly;
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        var monitoring = assembly.GetTypes().Where(t => t.Namespace == "WinMon.Monitoring").ToArray();
        Assert.That(monitoring, Is.Not.Empty);
        foreach (var type in monitoring)
        {
            Check(type.BaseType, type.FullName!, violations);
            foreach (var contract in type.GetInterfaces()) Check(contract, type.FullName!, violations);
            foreach (var field in type.GetFields(Declared)) Check(field.FieldType, $"{type.FullName}.{field.Name}", violations);
            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                var where = $"{type.FullName}.{method.Name}";
                if (method is MethodInfo info) Check(info.ReturnType, where, violations);
                foreach (var parameter in method.GetParameters()) Check(parameter.ParameterType, where, violations);
                foreach (var member in BodyReferences(method)) Check(member, where, violations);
            }
        }

        Assert.That(violations, Is.Empty);
    }

    private static void Check(MemberInfo? member, string where, ISet<string> violations)
    {
        switch (member)
        {
            case null:
                return;
            case Type type:
                if (type.HasElementType) Check(type.GetElementType(), where, violations);
                if (type.IsGenericType && !type.IsGenericTypeDefinition)
                    foreach (var argument in type.GetGenericArguments()) Check(argument, where, violations);
                if (type.IsGenericParameter) return;
                if (Forbidden(type)) violations.Add($"{where} -> {type.FullName}");
                return;
            default:
                Check(member.DeclaringType, where, violations);
                if (member is FieldInfo field) Check(field.FieldType, where, violations);
                return;
        }
    }

    private static bool Forbidden(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? "";
        if (UiAssemblies.Any(ui => assemblyName == ui || assemblyName.StartsWith(ui + ".", StringComparison.Ordinal)))
            return true;
        if (type.Assembly != typeof(ProcessRanking).Assembly) return false;
        var root = type;
        while (root.DeclaringType is not null) root = root.DeclaringType;
        // Global types in WinMon are compiler-generated: fixed buffers, string literal data and similar.
        return root.Namespace is not null and not "WinMon.Monitoring" and not "WinMon.Resources";
    }

    private static IEnumerable<MemberInfo> BodyReferences(MethodBase method)
    {
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il) yield break;
        var typeArguments = method.DeclaringType?.GetGenericArguments();
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var offset = 0; offset < il.Length;)
        {
            var value = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
            offset += il[offset] == 0xFE ? 2 : 1;
            var opCode = OpCodesByValue[value];
            switch (opCode.OperandType)
            {
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok:
                    var resolved = method.Module.ResolveMember(BitConverter.ToInt32(il, offset), typeArguments, methodArguments);
                    if (resolved is not null) yield return resolved;
                    offset += 4;
                    break;
                case OperandType.InlineSwitch:
                    offset += 4 + 4 * BitConverter.ToInt32(il, offset);
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    offset += 8;
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    offset += 1;
                    break;
                case OperandType.InlineVar:
                    offset += 2;
                    break;
                case OperandType.InlineNone:
                    break;
                default:
                    offset += 4;
                    break;
            }
        }
    }
}
