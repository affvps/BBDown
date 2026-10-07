using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BBDown.Commands;
using Spectre.Console.Cli;

namespace BBDown.Tests;

/// <summary>
/// 本仓命令行 Settings 类型的**唯一真相**（RF-95）。
///
/// 此前"全部命令的 Settings 类型"这份清单在三个地方各自硬编码：<c>CliOptionIndex</c> 的扫描清单、
/// <c>AotCliBindingTests.SettingsTypes</c> 与 <c>CliArgJoinerTests.AllSettingsTypes</c>。
/// 于是"新增命令时的完整性"实际无人守：清单内被删会被对拍用例发现，但**新增 Settings 类时三处
/// 一起漏、测试仍全绿**——`OptionIndex_CoversEverySettingsType` 注释里宣称的"少扫即失败"并不成立
/// （该用例是硬编码清单 vs 自身）。改为反射枚举程序集内全部 <see cref="CommandSettings"/> 派生
/// 类型：新增命令而没进 <c>CliOptionIndex</c> / 没被 <c>[DynamicDependency]</c> root，会在
/// <c>CliOptionIndexTests</c> / <c>AotCliBindingTests</c> 直接失败。
/// </summary>
internal static class SettingsTypeCatalog
{
    /// <summary>
    /// 非 CLI 命令模型的 <see cref="CommandSettings"/> 派生类型（经核实后显式排除，新增需在此登记并说明理由）：
    /// <see cref="ServeRequestOptions"/> 是 serve 模式 <c>/add-task</c> 的 HTTP 请求体 DTO——由
    /// System.Text.Json 源生成上下文（<c>[JsonSerializable]</c>）绑定，不经 Spectre，因此既不进
    /// <c>CliOptionIndex</c>，也不需要 <c>[DynamicDependency]</c> root。
    /// </summary>
    private static readonly HashSet<Type> NonCliTypes = [typeof(ServeRequestOptions)];

    /// <summary>程序集内全部参与 CLI 命令模型的 Settings 类型（按 FullName 排序，结果稳定）。</summary>
    internal static IReadOnlyList<Type> All { get; } = Discover();

    /// <summary><see cref="Program.Main"/> 上以 <c>[DynamicDependency]</c> root 的类型。</summary>
    internal static IReadOnlySet<Type> AotRootedTypes { get; } = DiscoverAotRoots();

    private static List<Type> Discover() =>
        typeof(MyOption).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(CommandSettings).IsAssignableFrom(t))
            .Where(t => !NonCliTypes.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    private static HashSet<Type> DiscoverAotRoots()
    {
        var main = typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static);
        if (main is null) return [];

        return main.GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(a => a.Type)
            .OfType<Type>()
            .ToHashSet();
    }
}
