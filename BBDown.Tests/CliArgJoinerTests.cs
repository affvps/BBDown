using System.Reflection;
using BBDown.Commands;
using Spectre.Console.Cli;

namespace BBDown.Tests;

/// <summary>
/// `BBDown sub add mid:19231317 --name "-尾野"` 此前报
/// <c>Option 'name' is defined but no value has been provided.</c>——
/// Spectre.Console.Cli 的 tokenizer 把任何以 '-' 开头的 argv 一律判为选项
/// （其 <c>CommandTreeTokenizer.ScanOptions</c> 没有转义写法），值以 '-' 开头的
/// 场景因此无法用空格写法表达。CliArgJoiner 在 argv 预处理阶段补齐 GNU getopt
/// 语义（把该值并入前一个选项的等号写法），这些用例既钉住"能合并"，
/// 也钉住"不误合并相邻的合法选项"。
/// </summary>
public class CliArgJoinerTests
{
    private static string[] Join(params string[] args) => CliArgJoiner.JoinDashLeadingOptionValues(args);

    [Fact]
    public void DashLeadingValue_IsJoinedIntoLongOption()
    {
        // 用户实际报出的命令：`sub add mid:19231317 --name "-尾野"`
        Assert.Equal(
            ["sub", "add", "mid:19231317", "--name=-尾野"],
            Join("sub", "add", "mid:19231317", "--name", "-尾野"));
    }

    [Fact]
    public void DashLeadingValue_IsJoinedIntoShortOption()
    {
        // 短别名同样按 ChOptionIndex 的 canonical 判定，不能只覆盖长名
        Assert.Equal(["-e=-hevc"], Join("-e", "-hevc"));
        Assert.Equal(["sub", "check", "-q=-1080P"], Join("sub", "check", "-q", "-1080P"));
    }

    [Fact]
    public void ValueContainingSeparator_IsJoinedAndKeptIntact()
    {
        // 值里再含 '=' 无妨：Spectre 只把第一个分隔符当"选项名/值"的分界
        Assert.Equal(["--name=-a=b"], Join("--name", "-a=b"));
    }

    [Fact]
    public void LoneDash_IsJoinedAsValue()
    {
        // 单独一个 "-" 也是以 '-' 开头的 token（Spectre 会抛 OptionHasNoName），
        // 当作值合并比报"选项没名字"更贴近用户意图
        Assert.Equal(["--name=-"], Join("--name", "-"));
    }

    [Fact]
    public void ConsecutiveFlags_AreNotJoined()
    {
        // 回归护栏：bool 开关后面跟选项是合法写法，合并会把它变成
        // "Flags cannot be assigned a value." 而破坏原本可用的命令行
        Assert.Equal(["--skip-mux", "--skip-subtitle"], Join("--skip-mux", "--skip-subtitle"));
        Assert.Equal(["-a", "-t", "--use-intl-api"], Join("-a", "-t", "--use-intl-api"));
    }

    [Fact]
    public void KnownOptionAfterValueOption_KeepsSpectreError()
    {
        // `--name --cookie` 是"漏写了 name 的值"：不合并，保留 Spectre 原本的
        // "no value has been provided"（否则 --cookie 会被静默吞成订阅名）
        Assert.Equal(["--name", "--cookie", "x"], Join("--name", "--cookie", "x"));
        Assert.Equal(["--name", "-c", "x"], Join("--name", "-c", "x"));
    }

    [Fact]
    public void UnknownDashTokenAfterFlag_IsLeftAlone()
    {
        // 开关不消耗值：后面的未知 token 保持原样（Spectre 会把它收进 Remaining），
        // 不能因为"看起来像值"就并入开关
        Assert.Equal(["--skip-mux", "-尾野"], Join("--skip-mux", "-尾野"));
    }

    [Fact]
    public void TokenAlreadyCarryingValue_IsNotJoined()
    {
        // '=' / ':' 两种自带值写法：该 token 不再吃下一个 argv
        Assert.Equal(["--name=-尾野", "-x"], Join("--name=-尾野", "-x"));
        Assert.Equal(["--name:-尾野", "-x"], Join("--name:-尾野", "-x"));
    }

    [Fact]
    public void NonDashValueAndEmptyValue_AreLeftAlone()
    {
        Assert.Equal(["--name", "尾野"], Join("--name", "尾野"));
        // 空串是 Spectre 认可的合法值（Kind.String），无需合并
        Assert.Equal(["--name", ""], Join("--name", ""));
    }

    [Fact]
    public void NothingToJoin_ReturnsSameInstance()
    {
        // 未发生合并时原样返回入参，避免无谓分配（调用方在 Main 的热路径上）
        var args = new[] { "sub", "add", "mid:1", "--name", "尾野" };
        Assert.Same(args, CliArgJoiner.JoinDashLeadingOptionValues(args));
    }

    [Theory]
    [InlineData("--name")]        // i + 1 越界
    [InlineData("")]
    public void TrailingOrOddTokens_DoNotThrow(string only)
    {
        Assert.Equal([only], Join(only));
    }

    /// <summary>全部命令的 Settings 类型（与 AotCliBindingTests 同一清单）。</summary>
    private static readonly Type[] AllSettingsTypes =
    [
        typeof(MyOption),
        typeof(ServeSettings),
        typeof(LoginSettings),
        typeof(LiveSettings),
        typeof(ArticleSettings),
        typeof(WatchLaterSettings),
        typeof(SubAddSettings),
        typeof(SubListSettings),
        typeof(SubRemoveSettings),
        typeof(SubCheckSettings),
    ];

    /// <summary>
    /// 索引必须覆盖**全部命令**的 Settings：遗漏子命令会让 sub 的 <c>--name</c>
    /// 不再被识别为取值选项，本 PR 的修复对子命令整体失效（且失败方式是静默的——
    /// 只是回到旧的报错），因此这条清单同时钉住"没有多扫/少扫"。
    /// </summary>
    [Fact]
    public void OptionIndex_CoversEverySettingsType()
    {
        Assert.Equal(
            AllSettingsTypes.OrderBy(t => t.FullName),
            CliOptionIndex.ScannedTypes.OrderBy(t => t.FullName));
    }

    /// <summary>
    /// 取值选项（非 bool）必须能被合并、bool 开关必须不能——逐选项核对，
    /// 新增选项时若类型或声明写错会在这里失败，而不是等用户撞上"no value"报错。
    /// </summary>
    [Fact]
    public void EveryOption_JoinsExactlyWhenItTakesValue()
    {
        var checkedOptions = 0;

        foreach (var type in AllSettingsTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<CommandOptionAttribute>();
                if (attr is null) continue;

                var takesValue = prop.PropertyType != typeof(bool);
                foreach (var token in attr.LongNames.Select(n => "--" + n)
                             .Concat(attr.ShortNames.Select(n => "-" + n)))
                {
                    checkedOptions++;
                    Assert.Equal(takesValue, CliOptionIndex.TakesValue(token));

                    var expected = takesValue ? new[] { token + "=-尾野" } : new[] { token, "-尾野" };
                    Assert.Equal(expected, Join(token, "-尾野"));
                }
            }
        }

        // 防止清单被误改小/属性被改名后测试空转成假绿
        Assert.True(checkedOptions > 100, $"核对的选项 token 数异常偏少: {checkedOptions}");
    }
}
