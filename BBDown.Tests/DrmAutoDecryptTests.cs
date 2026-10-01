using BBDown.Core.Entity;

namespace BBDown.Tests;

/// <summary>
/// DRM 开箱即用与自动解密（本次特性）：
/// ① <see cref="MyOption.AutoDecryptDrm"/> 判定矩阵——默认开启；<c>--no-decrypt-drm</c> 关闭；
///    <c>--decrypt-drm</c>（旧脚本兼容）与手动 <c>--key</c>/<c>--kid</c> 覆盖关闭；
/// ② device.wvd 解析优先级（<c>--wvd-path</c> &gt; PATH/程序目录 &gt; 程序目录内置文件），
///    并钉住"内置 device.wvd 确实随构建产出"——发布包再次漏带该文件时本用例失败；
/// ③ mp4decrypt 解析与**下载前**预检（缺工具时在下载流之前失败，而不是下完几个 G 才发现）。
/// </summary>
public class DrmAutoDecryptTests
{
    // ── ① 自动解密判定 ──

    [Fact]
    public void AutoDecryptDrm_IsOnByDefault()
        => Assert.True(new MyOption().AutoDecryptDrm);

    [Fact]
    public void AutoDecryptDrm_NoDecryptFlag_Disables()
        => Assert.False(new MyOption { NoDecryptDrm = true }.AutoDecryptDrm);

    [Fact]
    public void AutoDecryptDrm_LegacyFlagOverridesNoDecrypt()
        => Assert.True(new MyOption { NoDecryptDrm = true, DecryptDrm = true }.AutoDecryptDrm);

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", "")]
    [InlineData("", "fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210")]
    public void AutoDecryptDrm_ManualKeyOrKid_CountsAsEnabled(string key, string kid)
        => Assert.True(new MyOption { NoDecryptDrm = true, DrmKeyHex = key, DrmKidHex = kid }.AutoDecryptDrm);

    /// <summary>
    /// CLI 契约：新开关必须被 argv 预处理器识别为"不取值的 bool 开关"，
    /// 否则 <c>--no-decrypt-drm</c> 会被当成取值选项吞掉下一个 token。
    /// </summary>
    [Fact]
    public void NoDecryptDrm_IsRegisteredAsBoolFlag()
    {
        Assert.True(CliOptionIndex.IsKnownOption("--no-decrypt-drm"));
        Assert.False(CliOptionIndex.TakesValue("--no-decrypt-drm"));
        Assert.True(CliOptionIndex.TryGetCanonical("--no-decrypt-drm", out var canonical));
        Assert.Equal(nameof(MyOption.NoDecryptDrm), canonical);
    }

    // ── ② device.wvd ──

    [Fact]
    public void BundledDeviceWvd_IsCopiedNextToExecutable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "device.wvd");
        Assert.True(File.Exists(path),
            $"内置 device.wvd 缺失：发布包必须把 device.wvd 与可执行文件一起分发（实际查找: {path}）");
        Assert.True(new FileInfo(path).Length > 0, "内置 device.wvd 是空文件");
    }

    [Fact]
    public void ResolveWvdPath_ExplicitPathWins()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wvd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var custom = Path.Combine(dir, "custom.wvd");
        File.WriteAllBytes(custom, [1, 2, 3]);
        try
        {
            Assert.Equal(custom, Program.ResolveWvdPath(new MyOption { WvdPath = custom }));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolveWvdPath_DefaultsToBundledFile()
    {
        var path = Program.ResolveWvdPath(new MyOption());

        Assert.EndsWith("device.wvd", path);
        Assert.True(File.Exists(path), $"默认应解析到内置 device.wvd，实际: {path}");
    }

    [Fact]
    public void ResolveWvdPath_NonExistentExplicitPath_FallsBackToBundled()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.wvd");

        var path = Program.ResolveWvdPath(new MyOption { WvdPath = missing });

        Assert.NotEqual(missing, path);
        Assert.True(File.Exists(path));
    }

    // ── ③ mp4decrypt 与下载前预检 ──

    [Fact]
    public void ResolveMp4DecryptPath_ExplicitExistingPathWins()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mp4d-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var fake = Path.Combine(dir, "mp4decrypt");
        File.WriteAllBytes(fake, [0]);
        try
        {
            Assert.Equal(fake, Program.ResolveMp4DecryptPath(new MyOption { Mp4decryptPath = fake }));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EnsureDrmToolsAvailable_NonDrmContent_Passes()
        => Assert.True(Program.EnsureDrmToolsAvailable(new ParsedResult { IsDrm = false }, new MyOption()));

    /// <summary>
    /// 发布包把 mp4decrypt 放在可执行文件同目录：必须能被自动发现（否则内置了也不会被用上）。
    /// 解析顺序是 PATH 优先、程序目录其次——本用例临时把 PATH 指向空目录以屏蔽系统安装版，
    /// 从而确定性地验证"程序目录发现"这一条（发布包与 CI 依赖它）。
    /// </summary>
    [Fact]
    public void ResolveMp4DecryptPath_FindsToolNextToExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "mp4decrypt.exe" : "mp4decrypt";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        var savedPath = Environment.GetEnvironmentVariable("PATH");
        var emptyDir = Path.Combine(Path.GetTempPath(), $"empty-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        var created = false;
        try
        {
            Environment.SetEnvironmentVariable("PATH", emptyDir);
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, [0]);
                created = true;
            }

            Assert.Equal(path, Program.ResolveMp4DecryptPath(new MyOption()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            if (created) File.Delete(path);
            Directory.Delete(emptyDir, true);
        }
    }

    [Fact]
    public void EnsureDrmToolsAvailable_AutoDecryptDisabled_Passes()
        => Assert.True(Program.EnsureDrmToolsAvailable(
            new ParsedResult { IsDrm = true, DrmTechType = 2 },
            new MyOption { NoDecryptDrm = true }));

    [Fact]
    public void DrmToolchainProblem_MissingMp4Decrypt_ReportsActionableError()
    {
        var problem = Program.DrmToolchainProblem(
            new ParsedResult { IsDrm = true, DrmTechType = 2 }, new MyOption(),
            mp4decrypt: null, wvdPath: "/nonexistent/device.wvd");

        Assert.NotNull(problem);
        Assert.Contains("mp4decrypt", problem);
        Assert.Contains("Bento4", problem);
    }

    [Fact]
    public void DrmToolchainProblem_MissingWvdWithoutManualKeys_ReportsActionableError()
    {
        var problem = Program.DrmToolchainProblem(
            new ParsedResult { IsDrm = true, DrmTechType = 2 }, new MyOption(),
            mp4decrypt: "/usr/bin/mp4decrypt",
            wvdPath: Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.wvd"));

        Assert.NotNull(problem);
        Assert.Contains("device.wvd", problem);
    }

    [Fact]
    public void DrmToolchainProblem_ManualKeys_BypassWvdRequirement()
    {
        var options = new MyOption { DrmKeyHex = "0123456789abcdef0123456789abcdef", DrmKidHex = "fedcba9876543210fedcba9876543210" };

        var problem = Program.DrmToolchainProblem(
            new ParsedResult { IsDrm = true, DrmTechType = 2 }, options,
            mp4decrypt: "/usr/bin/mp4decrypt",
            wvdPath: Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.wvd"));

        Assert.Null(problem);
    }

    [Fact]
    public void DrmToolchainProblem_NonDrmOrDisabled_ReturnsNull()
    {
        Assert.Null(Program.DrmToolchainProblem(new ParsedResult { IsDrm = false }, new MyOption(), null, ""));
        Assert.Null(Program.DrmToolchainProblem(
            new ParsedResult { IsDrm = true, DrmTechType = 2 }, new MyOption { NoDecryptDrm = true }, null, ""));
    }
}
