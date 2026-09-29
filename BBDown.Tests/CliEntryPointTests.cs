using BBDown;

namespace BBDown.Tests;

/// <summary>
/// RF-94：<c>CliArgJoiner</c> 此前只有**纯函数级**覆盖（<c>CliArgJoinerTests</c> 直调
/// <c>JoinDashLeadingOptionValues</c>），入口调用点（<c>Program.Main</c>）零覆盖——变异实验：
/// 把 Main 里的调用注释掉，750 例仍全绿，即"整个修复静默失效"这一最高概率回归没有任何防线。
///
/// 本类改为**进程内直调 <see cref="Program.Main"/>**，覆盖「argv → 配置合并 → 选项值并入 →
/// Spectre 解析 → 命令执行」整链（测试并行已全局关闭，进程内调用安全）。
/// </summary>
public class CliEntryPointTests
{
    /// <summary>
    /// 进程内直调 Main 会写订阅清单，必须把 <see cref="SubscriptionStore.StoreRoot"/> 指到临时目录
    /// （与 <c>SubscriptionStoreTests</c> 同款隔离），跑完删除，不污染安装目录。
    /// </summary>
    private sealed class TempStore : IDisposable
    {
        private readonly string _originalRoot = SubscriptionStore.StoreRoot;

        internal string Root { get; } =
            Path.Combine(Path.GetTempPath(), "bbdown-entry-" + Guid.NewGuid().ToString("N"));

        internal TempStore()
        {
            Directory.CreateDirectory(Root);
            SubscriptionStore.StoreRoot = Root;
        }

        public void Dispose()
        {
            SubscriptionStore.StoreRoot = _originalRoot;
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
        }
    }

    /// <summary>
    /// 用户实际报出的命令行：`sub add mid:19231317 --name "-尾野"`。
    /// 修复前 Spectre 会把 `-尾野` 当成选项，报 "Option 'name' is defined but no value has been provided."
    /// 并以 1 退出；修复后该值必须真正落到订阅清单里。
    /// </summary>
    [Fact]
    public async Task Main_DashLeadingOptionValue_ReachesSubscriptionStore()
    {
        using var store = new TempStore();

        var exit = await Program.Main(["sub", "add", "mid:19231317", "--name", "-尾野"]);

        Assert.Equal(0, exit);
        var subs = await SubscriptionStore.LoadAsync();
        Assert.Contains(subs, s => s.Target == "mid:19231317" && s.Name == "-尾野");
    }

    /// <summary>
    /// 配置文件侧同一缺陷：BBDown.config 里的 `--work-dir` + `-wdtest` 会以
    /// "Option 'work-dir' is defined but no value has been provided." 失败（实测于基线 10e1049）。
    /// 合并发生在 Main 内，因此这里同时断言"合并后的 argv 已并入"与"整链退出码为 0"。
    /// </summary>
    [Fact]
    public async Task Main_ConfigFileDashLeadingValue_IsJoinedAtEntry()
    {
        var cfg = Path.Combine(Path.GetTempPath(), $"bbdown-entry-{Guid.NewGuid():N}.config");
        await File.WriteAllTextAsync(cfg, "--work-dir\n-wdtest\n");
        try
        {
            // 合并 → 并入（断言组合结果，防止"配置文件没被读到"时用例假绿）
            var merged = await BBDownConfigParser.MergeWithConfigAsync(["--config-file", cfg, "--help"]);
            Assert.Contains("--work-dir=-wdtest", CliArgJoiner.JoinDashLeadingOptionValues([.. merged]));

            // 整链：解析成功才会走到 --help（解析失败由异常处理器返回 1）
            using var store = new TempStore();
            Assert.Equal(0, await Program.Main(["--config-file", cfg, "--help"]));
        }
        finally
        {
            File.Delete(cfg);
        }
    }

    /// <summary>
    /// 护栏回归：`--name --cookie x` 是"漏写了值"，不能把 `--cookie` 静默吞成订阅名——
    /// 必须保持非零退出码，且不写入任何订阅。
    /// </summary>
    [Fact]
    public async Task Main_KnownOptionAsValue_KeepsNoValueErrorAndWritesNothing()
    {
        using var store = new TempStore();

        var exit = await Program.Main(["sub", "add", "mid:19231317", "--name", "--cookie", "x"]);

        Assert.NotEqual(0, exit);
        Assert.Empty(await SubscriptionStore.LoadAsync());
    }
}
