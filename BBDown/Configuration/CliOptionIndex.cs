using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BBDown.Commands;
using Spectre.Console.Cli;

namespace BBDown;

/// <summary>
/// 命令行选项索引：把用户实际书写的选项 token（<c>--long</c> / <c>-short</c>）映射到
/// Settings 类的属性名（canonical），并标出其中不消耗值的 bool 开关。
///
/// 反射在这里是既定做法而非新引入的风险：Spectre.Console.Cli 建立命令模型本身就走反射，
/// Program.cs 已用 DynamicDependency 逐个 root 下列 Settings 类型，原先
/// <c>BBDownConfigParser</c> 也以同一方式构建别名表（本类由此收口，供 argv 预处理与
/// 配置合并共用一份真相）。扫描写法与原来一致（<c>typeof(...)</c> 字面量逐个传入带
/// <see cref="DynamicallyAccessedMembersAttribute"/> 的形参），因此不产生裁剪警告。
///
/// 扫描范围必须覆盖**全部命令**的 Settings：遗漏子命令会让 sub/live/watchlater 的选项
/// 在 argv 预处理里被当成"未知 token"（例如 <c>--name</c> 不再被识别为取值选项）。
/// </summary>
internal static class CliOptionIndex
{
    /// <summary>token → canonical 属性名；只含本项目声明的选项，未知 token 不在其中。</summary>
    private static readonly Dictionary<string, string> AliasMap = new(StringComparer.Ordinal);

    /// <summary>不消耗值的选项（bool 开关）的 canonical 属性名。</summary>
    private static readonly HashSet<string> FlagCanonicals = new(StringComparer.Ordinal);

    /// <summary>已扫描的 Settings 类型（按声明顺序）。</summary>
    private static readonly List<Type> ScannedTypesList = [];

    static CliOptionIndex()
    {
        // 新增命令时补上其 Settings 类型（同一清单也出现在 AotCliBindingTests）。
        ScanOptionType(typeof(MyOption));
        ScanOptionType(typeof(ServeSettings));
        ScanOptionType(typeof(LoginSettings));
        ScanOptionType(typeof(LiveSettings));
        ScanOptionType(typeof(ArticleSettings));
        ScanOptionType(typeof(WatchLaterSettings));
        ScanOptionType(typeof(SubAddSettings));
        ScanOptionType(typeof(SubListSettings));
        ScanOptionType(typeof(SubRemoveSettings));
        ScanOptionType(typeof(SubCheckSettings));
    }

    /// <summary>token 是否是本项目声明的选项（<c>--long</c> / <c>-short</c> 的精确写法）。</summary>
    internal static bool IsKnownOption(string token) => AliasMap.ContainsKey(token);

    /// <summary>取 token 对应的 canonical 属性名。</summary>
    internal static bool TryGetCanonical(string token, [NotNullWhen(true)] out string? canonical)
        => AliasMap.TryGetValue(token, out canonical);

    /// <summary>
    /// token 是否会消耗下一个 argv 作为其值。
    /// bool 开关返回 false——开关后面跟以 '-' 开头的 token 必然是用户笔误而非值。
    /// </summary>
    internal static bool TakesValue(string token)
        => AliasMap.TryGetValue(token, out var canonical) && !FlagCanonicals.Contains(canonical);

    /// <summary>本索引覆盖的 Settings 类型（供测试核对清单完整性）。</summary>
    internal static IReadOnlyList<Type> ScannedTypes => ScannedTypesList;

    private static void ScanOptionType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        ScannedTypesList.Add(type);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<CommandOptionAttribute>();
            if (attr is null) continue;

            // 同一 token 在多个 Settings 里重复声明（如 -c|--cookie）时 canonical 名一致；
            // TryAdd 让先声明者胜出，真正的冲突由 Spectre 的命令模型裁决。
            foreach (var name in attr.LongNames)
            {
                AliasMap.TryAdd($"--{name}", prop.Name);
            }
            foreach (var name in attr.ShortNames)
            {
                AliasMap.TryAdd($"-{name}", prop.Name);
            }
            if (prop.PropertyType == typeof(bool))
            {
                FlagCanonicals.Add(prop.Name);
            }
        }
    }
}
