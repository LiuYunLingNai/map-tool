# BiliRemote · B 站快捷播放器

Windows 桌面程序，启动后默认打开 https://www.bilibili.com/。在内嵌网页中选择视频，可以通过按钮或全局快捷键控制播放。切换到其他应用、最小化窗口后，已注册的全局快捷键仍然有效；退出程序后会释放快捷键。

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl + Alt + 空格` | 播放 / 暂停，暂停保留播放位置 |
| `Ctrl + Alt + ←` | 后退，默认 10 秒 |
| `Ctrl + Alt + →` | 前进，默认 10 秒 |
| `Ctrl + Alt + S` | 停止：暂停并回到开头 |

底部可以把前进/后退步长改为 **5、10 或 30 秒**，也可以关闭全局快捷键。地址栏支持 B 站和 `b23.tv` 链接，支持返回、前进、首页和刷新。在 B 站点击视频时会在当前窗口打开。

点击底部的 **自定义快捷键**，再点击某个操作的输入框，直接按下要使用的按键并点击 **保存**。支持单独使用字母、数字、空格、方向键、Tab、Esc、功能键，也支持 Ctrl、Alt、Shift、Win 组合；程序不再要求修饰键。四个操作不能使用相同的快捷键，系统保留或被其他应用占用的组合以 Windows 实际注册结果为准。可点击 **恢复默认** 后保存。设置窗口打开期间会暂时停用本程序的全局快捷键，避免录入时误操作视频；取消后恢复原配置。全局单键会在其他应用中也触发，请按自己的使用习惯选择。

勾选 **窗口置顶** 后，播放器会保持在其他普通窗口上方；取消勾选即可恢复。快捷键、全局开关、窗口置顶、工具栏显示状态和跳转步长都会保存在本机，下次启动自动恢复。旧版设置文件会自动补充默认快捷键。

点击顶部 **收起工具栏**，地址栏、播放控制栏、设置和状态提示都会隐藏，网页占满窗口主体。**右键窗口标题栏 → 显示工具栏** 即可恢复；标题栏菜单也可以直接打开自定义快捷键或切换置顶。收起不会刷新网页或中断播放。简洁模式保留系统标题栏与边框，方便移动、缩放、最小化和关闭。

窗口大小限制从原来的 860 × 600 放宽为：工具栏显示时最小 **320 × 480**，收起时最小 **320 × 240**（WPF 逻辑像素）。仍可拖拽放大或最大化；小窗口的工具栏会换行，支持滚动。

## 运行

从 [GitHub Releases](https://github.com/LiuYunLingNai/map-tool/releases/latest) 下载 `BiliRemote-v0.1.0-win-x64.zip`，解压后双击 `BiliRemote/BiliRemote.exe`。Release 便携版已包含 .NET 运行环境，只需电脑已安装 WebView2。

打开 `artifacts/BiliRemote/BiliRemote.exe`。分发给别人时请复制整个 `BiliRemote` 文件夹，不能只复制 EXE。

- 系统：Windows 10 / 11，64 位。
- 浏览器组件：[Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)。多数 Windows 电脑已预装；程序会在缺失时提示。
- 默认构建使用 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。`build.ps1 -SelfContained` 可以生成包含 .NET 的版本，使用者无需另外安装 .NET。

首次打开视频时，请先在网页内点击播放一次；如果网页的自动播放策略限制播放，程序会提示这样操作。快捷键控制此程序中的视频。直播只在播放器支持回看时允许跳转，停止时会暂停并回到可跳转范围的起点。会员、登录、地区限制等由 B 站网页处理。

登录和设置保存在 `%LOCALAPPDATA%\BiliRemote`；无需在程序中填写账号或密码，直接在 B 站网页中登录。

## 开发和验证

使用 .NET 10 SDK，唯一第三方依赖为 Microsoft 官方 WebView2 SDK。NuGet 包缓存在项目的 `.packages` 目录。

```powershell
dotnet build .\BiliRemote.csproj
dotnet run --project .\BiliRemote.csproj
.\build.ps1
# 包含 .NET 的便携版本
.\build.ps1 -SelfContained
```

播放逻辑的测试使用 Node.js 自带的测试运行器，无需 npm 依赖：

```powershell
node --test .\tests\video-controller.test.cjs
dotnet run --project .\tests\SettingsVerification\SettingsVerification.csproj
```

真实 WebView2 集成验证使用本地生成的 90 秒静音媒体，验证播放、暂停、跳转、停止及 iframe 消息桥接，无需访问 B 站：

```powershell
dotnet run --project .\BiliRemote.csproj -- --self-test
```

报告输出到程序目录的 `self-test/results.txt`。集成验证会使用独立浏览器配置，并在完成后自动退出。

核心文件：`MainWindow.xaml` 为桌面界面，`MainWindow.xaml.cs` 负责网页和指令分发，`Services/HotkeyManager.cs` 使用 Windows `RegisterHotKey` 注册全局快捷键，`Browser/VideoController.js` 控制原生 `video`、`bwp-video` 及开放 Shadow DOM 中的视频。
