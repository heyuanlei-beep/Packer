# Packer — .NET 内网部署打包工具

基于**数据追加法（Overlay）**的 .NET 内网部署打包工具。

由两部分组成：

- **RuntimeStub**（Native AOT 外壳）：纯原生 EXE，零 .NET 依赖，负责检测/安装目标运行时并拉起业务程序。
- **Packer**（WinForms 打包工具）：读取业务 EXE，将 AOT 外壳、业务数据、动态配置打包成单一最终 EXE。

---

## 目录

- [功能特性](#功能特性)
- [项目结构](#项目结构)
- [文件尾部布局](#文件尾部布局)
- [构建要求](#构建要求)
- [构建步骤](#构建步骤)
- [使用方法](#使用方法)
- [运行时检测策略](#运行时检测策略)
- [注意事项](#注意事项)
- [日志排查](#日志排查)

---

## 功能特性

- **单一文件输出**：业务程序 + 运行时安装逻辑封装成一个 EXE。
- **Native AOT 外壳**：RuntimeStub 采用 `PublishAot=true` 编译，生成完全独立的原生 EXE（约 4.8MB），目标机器无需预装任何 .NET 运行时即可启动外壳。
- **动态配置注入**：从尾部 512 字节 KEY=VALUE 配置区读取 `VERSION`、`PARAM`、`URL`、`RUNTIME`。
- **运行时自动探测**：Packer 自动判断业务程序需要 `.NET Runtime` 还是 `.NET Desktop Runtime`。
- **多版本支持**：支持 `.NET 10.0` / `8.0` / `6.0` / `.NET Framework 4.8`。
- **静默安装**：根据版本自动使用 `/passive /norestart` 或 `/q /norestart`。
- **内置外壳**：RuntimeStub.exe 在编译 Packer 时通过 MSBuild Target 生成并嵌入 Packer，无需手动选择外壳路径。

---

## 项目结构

```text
Packer.slnx
├── RuntimeStub/           # Native AOT 外壳
│   ├── RuntimeStub.csproj
│   └── Program.cs         # 配置解析 / 运行时检测 / 下载安装 / 业务拉起
└── Packer/                # WinForms 打包工具
    ├── Packer.csproj
    ├── Program.cs
    ├── MainForm.cs        # 现代化 UI
    ├── PackEngine.cs      # 打包核心逻辑
    ├── IconChanger.cs     # 图标克隆 API（当前未使用）
    └── app.manifest
```

---

## 文件尾部布局

最终生成的 EXE 二进制结构：

```text
[Native AOT 外壳代码]
  + [业务 EXE 数据]
  + [512 字节 KEY=VALUE 配置区]
  + [8 字节业务 EXE 大小 (Int64 LE)]
```

配置区示例：

```text
VERSION=10.0;PARAM=/passive /norestart;URL=http://192.168.1.100/dotnet-runtime-10.0.0-win-x64.exe;RUNTIME=desktop
```

- `VERSION`：目标运行时版本（`10.0` / `8.0` / `6.0` / `FX48`）。
- `PARAM`：运行时安装包的静默安装参数。
- `URL`：内网运行时安装包下载地址。
- `RUNTIME`：业务程序所需运行时类型，`core` 或 `desktop`。

RuntimeStub 启动时：

1. 从倒数第 520 字节处读取 512 字节配置区，解析 KEY=VALUE。
2. 读取最后 8 字节得到业务 EXE 数据长度。
3. 向前偏移 `(8 + 512 + payloadSize)` 定位业务数据起点。
4. 提取业务 EXE 到 `<当前文件名>.real.exe` 并启动。

---

## 构建要求

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2022（**必须安装「C++ 桌面开发」工作负载**）
- MSVC 14.50+ 与 Windows SDK 10.0.26100.0+

> 注：Native AOT 编译依赖 C++ 链接器。如果未安装 C++ 工作负载，`PublishAot=true` 会失败。

---

## 构建步骤

```powershell
# 1. 还原依赖
dotnet restore Packer.slnx

# 2. 发布 AOT 外壳（可选，Packer 编译时会自动执行）
dotnet publish RuntimeStub/RuntimeStub.csproj -c Release -r win-x64

# 3. 构建 Packer
dotnet build Packer/Packer.csproj -c Release

# 4. 发布 Packer（框架依赖，目标机器需安装 .NET 10 Desktop Runtime 才能运行打包工具本身）
dotnet publish Packer/Packer.csproj -c Release -r win-x64 --self-contained false
```

编译后产物：

```text
Packer\bin\Release\net10.0-windows\win-x64\publish\Packer.exe
```

---

## 使用方法

1. 运行 `Packer.exe`。
2. 在 UI 中选择：
   - **目标运行时版本**：业务程序所需的 .NET 版本。
   - **运行时下载 URL**：内网可访问的运行时安装包 HTTP 地址。
   - **业务程序**：待打包的 .NET 业务 EXE。
   - **输出目录**：最终生成文件的位置。
3. 点击「生成动态外壳」。
4. 生成的最终 EXE 可直接复制到目标机器运行。

> 目标机器首次运行时：
> - 若已安装目标运行时，直接拉起业务程序。
> - 若未安装，从配置的 URL 下载并静默安装，然后拉起业务程序。

---

## 运行时检测策略

RuntimeStub 优先使用以下方式检测运行时：

1. 执行 `dotnet --list-runtimes` 并匹配 `Microsoft.NETCore.App` 或 `Microsoft.WindowsDesktop.App` 的版本前缀。
2. 若 `dotnet` 不可用或命令失败，回退到注册表检测：
   - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\`

对于 `.NET Framework 4.8`：

- 检测 `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full` 的 `Release` 值是否 ≥ 461808。

---

## 注意事项

- **图标克隆已禁用**：`UpdateResource` 会破坏 AOT 原生 EXE 的内部结构，因此不复制业务程序图标到最终 EXE。
- **业务 EXE 需为单文件发布**：建议将业务程序以 `PublishSingleFile=true` 发布后再作为输入，避免缺少依赖。
- **URL 必须可访问**：目标机器缺失运行时时，会从该 URL 下载安装包；地址不可访问会导致运行失败。
- **静默安装需要管理员权限**：首次安装运行时会触发 UAC 提权。
- **Packer 本身是 .NET 10 WinForms 程序**：开发/打包机器需安装 .NET 10 Desktop Runtime 才能运行 Packer；最终生成的业务外壳则不需要。

---

## 日志排查

RuntimeStub 运行日志写入目标机器的临时目录：

```text
%TEMP%\RuntimeStub.log
```

如果最终 EXE 双击无反应，优先查看该日志。

---

## 许可证

本项目仅供内部使用与学习交流。
