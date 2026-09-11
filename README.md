<img width="644" height="610" alt="屏幕截图 2026-09-11 151955" src="https://github.com/user-attachments/assets/0cfb2555-ce83-40b1-b926-26f12b71f5ab" />



# Native Pomodoro for Windows

一个轻量、原生、可换外观的 Windows 番茄钟。窗口直接沿图片轮廓显示，没有标题栏和方形背景；专注阶段按扇形平滑推进，休息阶段自动切换为黑白。

> 仓库只发布程序源码，不附带示例图片。请使用你自己拥有使用权的 PNG/JPG 图片构建，以免误用第三方图片或角色素材。

## 直接下载

不想自行编译，可以从 [Releases](https://github.com/Lddprogram/native-pomodoro-windows/releases/latest) 下载预构建的番茄主题 EXE。程序是绿色单文件版本，下载后直接双击运行，不需要安装。

预构建版本使用番茄外观；通过下方构建脚本可以换成自己的透明 PNG，同时保留相同的计时、轮廓窗口、扇形进度和统计功能。

Release 中还提供四个本地成品作为外观示例：

- `Tomato-Pomodoro.exe`：番茄外观
- `Earth-Kitty-Pomodoro.exe`：地球猫咪外观
- `Blueberry-Pomodoro.exe`：蓝莓外观
- `Pearl-Pomodoro.exe`：珍珠外观

这些附件用于展示同一套程序如何适配不同图片。示例图片不属于 MIT 源码许可证；再次发布或用于商业用途前，请自行确认相应图片的使用授权。

## 功能

- 两组节奏：`50 分钟专注 / 10 分钟休息`、`25 分钟专注 / 5 分钟休息`
- 执行次数：1、2、4、8 次或无限循环
- 自动记录今日、本周、本月、本年的专注时长
- 专注时显示原色，休息时自动变为黑白
- 180 到 680 像素的多档尺寸
- 可切换窗口始终置顶
- 无安装器、无联网、无遥测，编译后为单个 EXE
- Per-Monitor V2 高 DPI 支持


## 用 Agent 定制

> 可以将项目地址和图片丢给 Agent，让 Agent 完成你自己想要的番茄钟。

发送时可以直接说明希望使用的专注时长、休息时长、循环次数、默认大小、休息阶段颜色和是否默认置顶。Agent 可以克隆项目、处理图片透明背景，并运行 `build.ps1` 生成独立 EXE。

## 系统要求

- Windows 10 或 Windows 11
- Windows 自带的 .NET Framework 4.x C# 编译器
- PowerShell 5.1 或更高版本

Windows 11 通常不需要安装任何额外依赖。构建脚本会自动查找 64 位或 32 位的 `csc.exe`。

## 快速构建

1. 克隆仓库并进入目录。
2. 准备一张 PNG 或 JPG 图片。透明背景 PNG 的效果最好；纯白背景图片也会自动尝试抠图。
3. 在 PowerShell 中运行：

```powershell
.\build.ps1 -ImagePath "C:\图片\my-tomato.png" -Name "我的番茄钟"
```

生成文件位于 `dist\我的番茄钟.exe`。

若系统执行策略阻止脚本，可只为本次构建运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 `
  -ImagePath "C:\图片\my-tomato.png" `
  -Name "我的番茄钟"
```

## 图片建议

- 推荐尺寸：`1024 x 1024` 或更高
- 推荐格式：带透明背景的 PNG
- 主体尽量居中，并在四周保留少量透明空间
- 若使用白底图片，背景应接近纯白、颜色均匀
- 图片会作为资源嵌入 EXE，运行时不需要保留原图

程序会根据图片透明度创建不规则窗口。对于白底图片，会从边缘开始识别浅色背景并自动移除；复杂背景建议先在图片编辑软件中抠成透明 PNG。

## 操作说明

| 操作 | 功能 |
| --- | --- |
| 单击主体 | 开始或暂停计时 |
| 按住左键拖动 | 移动窗口 |
| 鼠标移到右上区域 | 显示三点菜单入口 |
| 右键单击 | 打开设置菜单 |

设置菜单可调整：节奏、循环次数、窗口尺寸、始终置顶，也可跳过当前阶段、重置或退出。

## 计时与循环规则

- 一次循环由一个专注阶段和一个休息阶段组成。
- `50 / 10` 模式的休息时间固定为 10 分钟。
- `25 / 5` 模式的休息时间固定为 5 分钟。
- 完成专注阶段时，专注分钟数会立即写入统计。
- 有限循环达到设定次数后停止；无限循环会持续运行，并在菜单中显示已完成次数。

## 数据与隐私

程序不访问网络。设置和统计保存在 `%LOCALAPPDATA%\NativeTomatoTimer\`：

- `settings.txt`：节奏、次数、尺寸、置顶等设置
- `stats.txt`：按日期记录的专注分钟数

卸载程序时可直接删除 EXE；如果也要清除统计，再删除上面的数据目录。

## 项目结构

```text
.
|-- native_timer.cs    # 原生窗口、计时、绘制、设置与统计
|-- app.manifest       # Windows 版本与高 DPI 声明
|-- build.ps1          # 单文件构建脚本
|-- LICENSE            # MIT License
`-- README.md          # 使用说明
```

## 从源码工作

`native_timer.cs` 使用 WinForms、GDI+ 和 `UpdateLayeredWindow` 绘制逐像素透明窗口。构建时，图片以 `TomatoImage` 的资源名嵌入程序集。

等价的手动编译命令如下：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
  /out:dist\NativePomodoro.exe `
  /win32manifest:app.manifest `
  /resource:"C:\图片\theme.png",TomatoImage `
  /reference:System.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  native_timer.cs
```

## 常见问题

### 双击后没有反应

在 PowerShell 中直接运行 EXE 以确认是否被 Windows 安全策略拦截。首次运行下载的文件时，也可以在文件属性中选择“解除锁定”。

### 图片周围有白边

优先使用透明背景 PNG。白底自动抠图适合纯色、均匀背景，阴影或压缩噪点可能被保留下来。

### 点击会穿透到后面的窗口

当前版本只把完全透明像素交给后方窗口，图片可见区域会正常接收单击和拖动。若自定义图片边缘透明度过低，先清理半透明杂边再重新构建。

### 高分屏上看起来模糊

请使用至少 `1024 x 1024` 的源图，并保留 `app.manifest` 参与构建。低分辨率图片放大后无法由程序恢复细节。

## 贡献

欢迎提交 Issue 和 Pull Request。修改后请至少验证：两种节奏切换、有限与无限循环、休息阶段黑白效果、拖动、尺寸切换、置顶状态持久化，以及 100%/150%/200% 缩放下的显示效果。

## 许可证

源码以 [MIT License](LICENSE) 开源。你用于构建的图片不自动获得 MIT 许可，其权利仍归图片权利人所有。
