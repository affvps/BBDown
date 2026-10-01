# Widevine DRM 原生解密指南 (DRM Decryption)

> 本文档介绍如何在 BBDown 中使用内置的原生 Widevine CDM 解密并下载哔哩哔哩受 DRM 保护的内容（如付费课程 Cheese、特定版权番剧等）。

---

## 1. 核心技术优势

传统工具通常需要额外安装 Python 运行时及第三方库（如 `pywidevine`、`protobuf`、`cryptography` 等）才能处理 Widevine DRM 协议。

BBDown 内置了**纯原生 C# 实现的 Widevine Content Decryption Module (CDM)**：
- **零外部环境依赖**：无需安装 Python、无需配置 pip 虚拟环境。
- **全自动化流程**：自动请求 B 站许可证服务器获取内容解密密钥，自动完成流分片解密，并无缝调用 FFmpeg 混流。
- **支持手动注入**：对于特殊离线场景，也支持手动通过 `--key` 和 `--kid` 传入已知密钥。

---

## 2. 准备：开箱即用（无需自备设备文件）

**发布包已内置 `device.wvd`**（Widevine L3 设备凭据），与可执行文件解压在同一目录即可，无需再自行提取或寻找设备文件。

BBDown 解析设备文件的优先级（前者优先）：
1. **`--wvd-path` 显式指定**（文件存在时采用）——内置证书被吊销/封禁时用它替换；
2. **环境变量 `PATH` / 程序目录**中的 `device.wvd`；
3. **程序目录内置的 `device.wvd`**（发布包自带，默认走这一条）。

**`mp4decrypt`（Bento4）同样已内置**在发布包与 Docker 镜像中（版本与归档 SHA256 在构建时固定校验），Windows / macOS / Linux **x64** 解压即用：

| 平台 | 内置 mp4decrypt |
|---|---|
| win-x64 / win-arm64 | ✅（arm64 包内为 x64 版，Windows 11 on ARM 经 x64 模拟运行） |
| linux-x64 | ✅ |
| linux-arm64 | ❌ 官方无 arm64 二进制：请自行安装或用 `--mp4decrypt-path` 指定 |
| osx-x64 / osx-arm64 | ✅（官方 universal 二进制） |
| Docker 镜像 | ✅ `/usr/local/bin/mp4decrypt` |

若缺失（例如 linux-arm64），BBDown 会在**下载流之前**报错并给出指引。想用自备版本时，`--mp4decrypt-path` 优先级最高。第三方分发与许可证说明见仓库根目录 `THIRD-PARTY-NOTICES.md`。

---

## 3. 自动解密下载实战

### 3.1 下载 DRM 付费课程（Cheese 课堂）
确保已登录拥有该课程购买权限的账号（执行过 `BBDown login`），**直接下载即可**——默认自动检测 DRM 并自动解密：

```bash
BBDown "https://www.bilibili.com/cheese/play/ep1243104"
```

**内部执行流程**：
1. 解析请求携带 `drm_tech_type=2`，响应中的 `is_drm` 即"是否 DRM"的判定来源。
2. 命中 DRM 时从内置 `device.wvd` 加载 CDM 客户端（下载流之前先检查 `mp4decrypt`/`device.wvd` 是否齐备）。
3. 向 B 站许可证服务器发送 Challenge 请求并获取解密密钥（Key & KID）。
4. 对下载的加密分片完成原生解密，并混流为标准 `.mp4` 文件。

### 3.2 关闭自动解密
不需要解密能力（或希望完全保持旧请求形态）时：

```bash
BBDown --no-decrypt-drm <URL>
```

此时不携带 `drm_tech_type=2`，遇到受 DRM 保护的内容按普通解析失败处理。旧脚本里的 `--decrypt-drm` 仍然有效（等价于默认行为）。

---

## 4. 手动指定密钥模式

如果已通过其他方式获取了该视频的解密 Key 和 KID（Hex 字符串），可以直接手动传入：

```bash
BBDown --key "0123456789abcdef0123456789abcdef" --kid "fedcba9876543210fedcba9876543210" <URL>
```

---

## 5. 常见问题与排障

- **报错缺少 `mp4decrypt`**：
  受 DRM 保护的内容需要 Bento4 的 `mp4decrypt`。请从 [Bento4 releases](https://github.com/axiomatic-systems/Bento4/releases) 下载后放入 `PATH` 或程序目录，或用 `--mp4decrypt-path` 指定；BBDown 会在下载流之前就报错，不会浪费带宽。
- **报错找不到 `device.wvd`**：
  发布包内置该文件，通常是解压不完整（只取了可执行文件）。请确认 `device.wvd` 与可执行文件在同一目录，或用 `--wvd-path` 指定。
- **获取许可证返回 403 / 失败**：
  请检查当前登录账号是否已购买该课程/番剧。DRM 解密无法绕过账号的购买鉴权，必须拥有合法的播放权限。
- **部分超高清画质未下发密钥**：
  B 站部分最高画质仅向硬件 L1 设备下发密钥，L3 凭据通常可解密 1080P 及以下画质，此时程序会自动选择 L3 支持的最高画质。

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [批量下载与自动化脚本](Batch-and-Automation) | 📑 [返回目录](Home) | [API 服务器与 Docker 部署](API-Server-and-Docker) ➡️ |
