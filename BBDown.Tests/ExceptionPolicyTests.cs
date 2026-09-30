using System.ComponentModel;
using System.Security;
using System.Text;
using System.Xml;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// I7 异常过滤策略的**真值表**：每条谓词的类型集合必须与其声明逐字一致。
///
/// 为什么需要它：这些谓词被 64 个 <c>catch (Exception ex) when (…)</c> 站点共享，
/// 集合增删一个类型就等价于同时改变 44 个站点的捕获面（历史上正是这种"手工同步"造成了
/// `SubCommand` 与下载页过滤器的漂移）。本用例把每条策略的集合钉死——任何增删类型、
/// 或把近似集合当成同一条策略，都会立即失败。
/// </summary>
public class ExceptionPolicyTests
{
    /// <summary>
    /// 候选类型全集 = 全部策略声明的类型 + 对照类型（不得被任何策略匹配）。
    /// 对照类型覆盖库内实际出现过的近邻异常，防止"集合写宽"被漏检。
    /// </summary>
    private static readonly Type[] Universe =
    [
        // —— 被策略声明的类型 ——
        typeof(IOException),
        typeof(UnauthorizedAccessException),
        typeof(System.Text.Json.JsonException),
        typeof(HttpRequestException),
        typeof(KeyNotFoundException),
        typeof(TimeoutException),
        typeof(TaskCanceledException),
        typeof(InvalidOperationException),
        typeof(ArgumentException),
        typeof(InvalidDataException),
        // —— 对照类型（不得被任何策略匹配）——
        // 注意：派生自已声明类型的异常（如 ArgumentOutOfRangeException : ArgumentException）
        // 会被子类型敏感的 `is` 命中，属预期行为，故不放对照区。
        typeof(OperationCanceledException),
        typeof(AggregateException),
        typeof(FormatException),
        typeof(OverflowException),
        typeof(NotSupportedException),
        typeof(XmlException),
        typeof(DecoderFallbackException),
        typeof(Win32Exception),
        typeof(SecurityException),
        typeof(ArgumentOutOfRangeException),
        typeof(IndexOutOfRangeException),
        typeof(UriFormatException),
        typeof(NullReferenceException),
        typeof(System.Security.Cryptography.CryptographicException),
    ];

    /// <summary>策略名 → （谓词，声明的精确集合）。新增/删除策略必须同步本表。</summary>
    private static readonly Dictionary<string, (Func<Exception, bool> Predicate, Type[] Types)> Policies = new(StringComparer.Ordinal)
    {
        ["IsBestEffortFailure"] = (ExceptionPolicies.IsBestEffortFailure,
            [typeof(IOException), typeof(UnauthorizedAccessException)]),
        ["IsJsonOrIoFailure"] = (ExceptionPolicies.IsJsonOrIoFailure,
            [typeof(IOException), typeof(System.Text.Json.JsonException)]),
        ["IsSubtitleFetchFailure"] = (ExceptionPolicies.IsSubtitleFetchFailure,
            [typeof(HttpRequestException), typeof(System.Text.Json.JsonException), typeof(KeyNotFoundException), typeof(TimeoutException)]),
        ["IsTransportFailure"] = (ExceptionPolicies.IsTransportFailure,
            [typeof(HttpRequestException), typeof(IOException), typeof(TaskCanceledException)]),
        ["IsMissingResponseNodeFailure"] = (ExceptionPolicies.IsMissingResponseNodeFailure,
            [typeof(KeyNotFoundException), typeof(InvalidOperationException)]),
        ["IsTaskStoreFailure"] = (ExceptionPolicies.IsTaskStoreFailure,
            [typeof(IOException), typeof(UnauthorizedAccessException), typeof(System.Text.Json.JsonException)]),
        ["IsParseDowngradeFailure"] = (ExceptionPolicies.IsParseDowngradeFailure,
            [typeof(HttpRequestException), typeof(System.Text.Json.JsonException), typeof(InvalidOperationException),
             typeof(TimeoutException), typeof(TaskCanceledException)]),
        ["IsProbeRequestFailure"] = (ExceptionPolicies.IsProbeRequestFailure,
            [typeof(HttpRequestException), typeof(System.Text.Json.JsonException), typeof(KeyNotFoundException),
             typeof(InvalidOperationException), typeof(TimeoutException)]),
        ["IsSkippableItemFailure"] = (ExceptionPolicies.IsSkippableItemFailure,
            [typeof(HttpRequestException), typeof(System.Text.Json.JsonException), typeof(KeyNotFoundException),
             typeof(InvalidOperationException), typeof(IOException), typeof(UnauthorizedAccessException),
             typeof(ArgumentException), typeof(TimeoutException), typeof(TaskCanceledException), typeof(InvalidDataException)]),
    };

    /// <summary>
    /// 逐策略 × 逐类型对拍：只有"声明的集合（含其子类）"内为 true，集合外全为 false。
    /// 期望值按 <see cref="Type.IsAssignableFrom"/> 计算——C# 的 <c>ex is T</c> 是**子类型敏感**的，
    /// 例如 `ArgumentException` 会命中 `ArgumentOutOfRangeException`；用精确集合比对会误报。
    /// </summary>
    [Fact]
    public void EveryPolicy_MatchesExactlyItsDeclaredTypeSet()
    {
        var failures = new List<string>();

        foreach (var (name, (predicate, types)) in Policies)
        {
            foreach (var type in Universe)
            {
                var instance = (Exception)Activator.CreateInstance(type)!;
                var expected = types.Any(declared => declared.IsAssignableFrom(type));
                var actual = predicate(instance);
                if (expected != actual)
                    failures.Add($"{name}({type.Name}) = {actual}，期望 {expected}");
            }
        }

        Assert.True(failures.Count == 0,
            "异常策略的集合与其声明不一致（增删类型必须同步谓词与本表）：\n  " + string.Join("\n  ", failures));
    }

    /// <summary>谓词条数钉住：批次内静默删除谓词会让共享站点失去命名策略。</summary>
    [Fact]
    public void PolicyCount_IsPinned() => Assert.Equal(9, Policies.Count);

    /// <summary>全集必须覆盖所有声明的类型，否则"集合写宽"可能被漏检（比对上一条更强的防假绿）。</summary>
    [Fact]
    public void Universe_CoversEveryDeclaredType()
    {
        var declared = Policies.SelectMany(p => p.Value.Types).ToHashSet();
        var missing = declared.Except(Universe).Select(t => t.Name).ToList();

        Assert.True(missing.Count == 0, $"以下类型被策略声明但不在 Universe 中，真值表无法覆盖：{string.Join(", ", missing)}");
    }
}
