# Muse Desktop Account Manager

**v1.0.0 · Windows 桌面端 · 非官方项目**

一个本地 Muse 网页账号管理器。每个账号使用独立的 WebView2 浏览器目录，用户手动切换，右侧直接显示 Muse 官方网页。项目不提供 Muse API、自动登录、自动发送消息或额度轮换。

## 下载与使用

1. 在 GitHub 的 **Releases** 页面下载 `MuseDesktop-v1.0.0-win-x64.zip`，解压到可写的普通目录。
2. 双击 `启动Muse.cmd`，或运行 `app/MuseDesktop.exe`。
3. 点击“添加账号”，输入仅保存在本机的备注，然后在 Muse 官方网页自行登录。
4. 点击侧栏账号即可手动切换；一次只打开一个账号的网页。

系统要求：Windows x64、.NET Framework 4.8、Microsoft Edge WebView2 Runtime。若缺少 WebView2 Runtime，请从 [微软官方下载页](https://developer.microsoft.com/microsoft-edge/webview2/)安装。无需 Python、API Key 或管理员权限。

## 功能

- 账号添加、重命名、删除与手动切换；不同账号的浏览器状态隔离。
- 网页原生聊天、模型选择、云盘与记忆功能由 Muse 官方网页负责。
- 浅色/深色外观、网页主题联动、80%–160% 网页缩放。
- 可收起账号栏；记住窗口尺寸、最大化状态和账号栏状态。
- 设置中可关闭默认开启的缓存策略：切换账号后，对所有已有账号清理 **24 小时前的 WebView2 网页磁盘缓存**。

## 数据与隐私

账号数据位于 `%LOCALAPPDATA%\MuseDesktop`，不在程序目录：

| 路径 | 内容 |
| --- | --- |
| `accounts.json` | 随机账号标识、用户备注、安全的 Muse 对话路径 |
| `settings.json` | 外观、缩放、窗口和缓存设置 |
| `profiles/<ID>` | 各账号独立的 WebView2 登录状态与网页数据 |

更新或替换程序时保留上述目录；**不要把它复制到公开仓库或分享给别人**。删除账号会清理该账号的本地浏览器目录，但不会删除 Muse 云端账号或对话。缓存策略只调用 WebView2 的 `DiskCache` 类别，不清理 Cookie 和网站存储；网站存储及其他浏览器数据不受 24 小时规则约束。较旧网页资源下次打开时可能重新下载。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop-client\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\desktop-client\RunTests.ps1
```

构建脚本使用系统自带的 .NET Framework 编译器和仓库内 WebView2 SDK 文件，输出到 `app/`。源码目标为 C# 5 / Windows x64；构建时请先关闭正在运行的客户端。测试只使用本地模拟网页，不登录真实账号，也不发送 Muse 模型请求。

## 范围与许可

这是非官方客户端。Muse 网站可能改变页面控件或限制嵌入浏览器登录；真实 Muse 聊天和 Google 云盘授权并非本项目的自动化验收项。项目不绕过登录、限额或访问控制。

本项目自有代码、文档与中性图标采用 [MIT 许可证](LICENSE)。WebView2 的许可与第三方声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 及下载包中的 `app/LICENSE.txt`、`app/NOTICE.txt`。
