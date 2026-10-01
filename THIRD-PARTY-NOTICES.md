# 第三方组件声明

本仓库自身以 MIT 许可证发布（见 [LICENSE](LICENSE)）。发布产物（Release 压缩包、Docker 镜像）**额外分发**下列第三方二进制/数据文件，它们各自的许可证独立于本项目：

## 1. mp4decrypt（Bento4）

- **用途**：Widevine DRM 内容解密。BBDown 以独立进程调用该工具（非链接），属"聚合分发"。
- **来源**：Bento4 SDK 官方二进制 <https://www.bok.net/Bento4/binaries/>（项目主页 <https://www.bento4.com/>，源码 <https://github.com/axiomatic-systems/Bento4>）
- **内置版本**：`1-6-0-641`（归档 SHA256 固定在 `.github/workflows/release.yml`、`build_latest.yml` 与 `Dockerfile` 的 `BENTO4_*` 变量中，构建时校验，防篡改/静默换包）
- **分发范围**：
  | 产物 | 是否内置 mp4decrypt |
  |---|---|
  | `win-x64` / `win-arm64` zip | ✅（arm64 包内为 x64 版，Windows 11 on ARM 经 x64 模拟运行） |
  | `linux-x64` zip | ✅ |
  | `linux-arm64` zip | ❌（Bento4 官方未提供 arm64 Linux 二进制；请自行安装或用 `--mp4decrypt-path` 指定） |
  | `osx-x64` / `osx-arm64` zip | ✅（官方 universal 二进制，两个架构通用） |
  | Docker 镜像（linux-x64） | ✅（`/usr/local/bin/mp4decrypt`） |
- **许可证**：GNU General Public License v2.0（Bento4 亦提供商业授权，见其官网）。许可证全文随二进制一同分发：
  - zip 内的 `mp4decrypt-LICENSE.txt`（取自 Bento4 SDK 的 `docs/LICENSE.txt`）；
  - Docker 镜像内的 `/app/mp4decrypt-LICENSE.txt`。
- **获取对应源码**：<https://github.com/axiomatic-systems/Bento4>（tag 与内置版本对应）。

## 2. device.wvd（Widevine L3 设备凭据）

- **用途**：构造 Widevine CDM 客户端（L3），用于向 B 站许可证服务器请求解密密钥。
- **来源**：本仓库自带（`BBDown.Core/device.wvd`），随发布产物分发；`--wvd-path` 可替换为自备设备文件。
- **说明**：这是设备凭据数据文件而非可执行程序；若其证书被服务端吊销/封禁，请用 `--wvd-path` 指向自备文件。

---

> 修改内置版本或分发范围时，请同步更新本文件、`README.md` 的 DRM 小节与 wiki「Widevine DRM 原生解密指南」。
