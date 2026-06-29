using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace RuntimeStub;

/// <summary>
/// 外壳模板 —— Native AOT 编译的纯原生程序。
/// 职责：从尾部解析动态配置 → 检查目标 .NET 环境 → 缺失则从配置的 URL 下载安装 → 提取自身尾部追加的业务 EXE → 拉起业务程序。
///
/// 文件尾部布局（v3.0 多字段动态配置版）：
///   [原生代码] + [业务 EXE 数据] + [512 字节 KEY=VALUE 配置区] + [8 字节业务 EXE 大小 (Int64 LE)]
///
/// 配置区格式（UTF-8）：
///   VERSION=10.0;PARAM=/passive /norestart;URL=http://...
/// </summary>
internal static class Program
{
    /// <summary>
    /// 动态配置区固定大小（字节）
    /// </summary>
    private const int ConfigBufferSize = 512;

    /// <summary>
    /// 尾部长度标识大小（字节）
    /// </summary>
    private const int TrailerSize = 8;

    /// <summary>
    /// 从文件尾部解析出的运行时下载地址（打包时注入）
    /// </summary>
    private static string _runtimeUrl = "";

    /// <summary>
    /// 目标运行时版本（例如 "10.0" / "8.0" / "6.0" / "FX48"）
    /// </summary>
    private static string _targetVersion = "10.0";

    /// <summary>
    /// 安装包静默安装参数（打包时注入）
    /// </summary>
    private static string _silentArgs = "/passive /norestart";

    /// <summary>
    /// 目标运行时类型（"core" 表示 .NET Runtime，"desktop" 表示 .NET Desktop Runtime）
    /// </summary>
    private static string _runtimeType = "core";

    /// <summary>
    /// 临时下载路径（存放在系统 Temp 目录）
    /// </summary>
    private static readonly string TempInstallerPath = Path.Combine(Path.GetTempPath(), "dotnet_install_overlay.exe");

    // ================================================================
    // 入口
    // ================================================================
    private static async Task Main(string[] args)
    {
        try
        {
            // 1. 从自身文件尾部解析出打包时注入的动态配置信息
            if (!ParseDynamicConfig())
            {
                Log("未检测到有效的动态配置（可能是裸壳），退出。");
                return;
            }

            Log($"动态配置解析成功，目标版本: {_targetVersion}, 运行时类型: {_runtimeType}, 下载地址: {_runtimeUrl}, 安装参数: {_silentArgs}");

            // 2. 检查并全自动安装目标运行时（使用动态解析出的版本和地址）
            if (!IsDotNetInstalled())
            {
                Log($"目标运行时 {_targetVersion} 未检测到，开始从内网下载...");
                if (await DownloadRuntimeAsync())
                {
                    Log("下载完成，开始静默安装...");
                    ExecuteSilentInstall();
                    Log("安装流程结束。");
                }
                else
                {
                    Log("运行时下载失败，安全退出。");
                    return;
                }
            }

            // 3. 提取自身尾部追加的业务 EXE 数据并运行
            LaunchBusinessApp(args);
        }
        catch (Exception ex)
        {
            Log($"致命错误: {ex.Message}");
        }
    }

    // ================================================================
    // 核心升级：从文件末尾逆向读取并解析多字段动态配置
    // ================================================================
    private static bool ParseDynamicConfig()
    {
        try
        {
            string? currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
                return false;

            using FileStream fs = new(currentExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // 安全边界检查：至少需要 520 字节（512 字节配置 + 8 字节长度）
            if (fs.Length < ConfigBufferSize + TrailerSize)
                return false;

            // 指针移动到配置区的起点（倒数第 520 个字节处，因为最后 8 字节是大小标识）
            fs.Seek(-(ConfigBufferSize + TrailerSize), SeekOrigin.End);

            byte[] configBytes = new byte[ConfigBufferSize];
            fs.ReadExactly(configBytes, 0, ConfigBufferSize);

            // 将二进制转换为字符串，并修剪掉末尾用来填充的空字符 (\0)
            string rawConfig = Encoding.UTF8.GetString(configBytes).TrimEnd('\0');

            if (string.IsNullOrWhiteSpace(rawConfig))
                return false;

            // 解析 KEY=VALUE;KEY=VALUE;... 格式
            var parsed = ParseKeyValueConfig(rawConfig);

            if (!parsed.TryGetValue("URL", out string? url) ||
                string.IsNullOrWhiteSpace(url) ||
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _runtimeUrl = url;

            if (parsed.TryGetValue("VERSION", out string? version) && !string.IsNullOrWhiteSpace(version))
            {
                _targetVersion = version.Trim();
            }

            if (parsed.TryGetValue("PARAM", out string? param) && !string.IsNullOrWhiteSpace(param))
            {
                _silentArgs = param.Trim();
            }

            if (parsed.TryGetValue("RUNTIME", out string? runtimeType) && !string.IsNullOrWhiteSpace(runtimeType))
            {
                _runtimeType = runtimeType.Trim();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseKeyValueConfig(string rawConfig)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawConfig))
            return result;

        // 按分号分隔字段，支持 VALUE 内部不带分号的情况
        string[] pairs = rawConfig.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
                continue;

            string key = pair.Substring(0, eq).Trim();
            string value = pair.Substring(eq + 1).Trim();
            result[key] = value;
        }

        return result;
    }

    // ================================================================
    // 提取尾部业务数据并拉起
    // ================================================================
    private static void LaunchBusinessApp(string[] args)
    {
        try
        {
            string? currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
                return;

            string currentDir = AppContext.BaseDirectory;
            string currentExeName = Path.GetFileNameWithoutExtension(currentExePath);
            string targetRealExePath = Path.Combine(currentDir, $"{currentExeName}.real.exe");

            using FileStream fs = new(currentExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // 最后 8 个字节是业务 EXE 的数据长度
            fs.Seek(-TrailerSize, SeekOrigin.End);
            byte[] sizeBytes = new byte[TrailerSize];
            fs.ReadExactly(sizeBytes, 0, TrailerSize);
            long payloadSize = BitConverter.ToInt64(sizeBytes, 0);

            // 基本合理性校验
            if (payloadSize <= 0 || payloadSize > fs.Length - ConfigBufferSize - TrailerSize)
            {
                Log($"业务数据大小校验失败: payloadSize={payloadSize}, fileSize={fs.Length}");
                return;
            }

            // 指针向前移动，跳过 8字节大小 + 512字节配置区 + 业务数据大小
            fs.Seek(-(TrailerSize + ConfigBufferSize + payloadSize), SeekOrigin.End);

            byte[] payloadData = new byte[payloadSize];
            fs.ReadExactly(payloadData, 0, (int)payloadSize);
            File.WriteAllBytes(targetRealExePath, payloadData);

            Log($"业务程序已释放: {targetRealExePath} ({payloadSize} bytes)");

            Process.Start(new ProcessStartInfo
            {
                FileName = targetRealExePath,
                Arguments = string.Join(" ", args),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"拉起业务程序失败: {ex.Message}");
        }
    }

    // ================================================================
    // 检测目标 .NET 是否已安装（根据配置区 VERSION 字段动态选择）
    // ================================================================
    private static bool IsDotNetInstalled()
    {
        try
        {
            // 如果打包工具指定的是传统 .NET Framework 4.8
            if (string.Equals(_targetVersion, "FX48", StringComparison.OrdinalIgnoreCase))
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                if (key == null)
                {
                    Log("未找到 .NET Framework 4.8 注册表项。");
                    return false;
                }

                // 传统 Framework 通过 Release 字段判断，461808 代表 4.8
                int release = (int)(key.GetValue("Release") ?? 0);
                Log($".NET Framework Release 值: {release} (>= 461808 表示已安装 4.8)");
                return release >= 461808;
            }

            // 对于 .NET 6/8/10，最可靠的方式是执行 dotnet --list-runtimes
            string frameworkName = string.Equals(_runtimeType, "desktop", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.WindowsDesktop.App"
                : "Microsoft.NETCore.App";

            Log($"执行检测命令: dotnet --list-runtimes (查找 {frameworkName} {_targetVersion})");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-runtimes",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Log("无法启动 dotnet 进程，回退到注册表检测。");
                return CheckRegistryForNetCoreRuntime(_targetVersion, frameworkName);
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Log($"dotnet --list-runtimes 输出:\n{output}");
            if (!string.IsNullOrWhiteSpace(error))
            {
                Log($"dotnet --list-runtimes 错误输出: {error}");
            }

            // 例如输出: Microsoft.NETCore.App 10.0.0 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.0]
            // 只需要匹配前缀，比如 10.0
            if (output.Contains($"{frameworkName} {_targetVersion}", StringComparison.OrdinalIgnoreCase))
            {
                Log($"已检测到目标运行时 {frameworkName} {_targetVersion}");
                return true;
            }

            // 如果未匹配到，回退到注册表检测
            Log($"dotnet --list-runtimes 未找到 {frameworkName} {_targetVersion}，回退到注册表检测。");
            return CheckRegistryForNetCoreRuntime(_targetVersion, frameworkName);
        }
        catch (Exception ex)
        {
            Log($"检测运行时异常: {ex.Message}，回退到注册表检测。");
            return CheckRegistryForNetCoreRuntime(_targetVersion, string.Equals(_runtimeType, "desktop", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.WindowsDesktop.App" : "Microsoft.NETCore.App");
        }
    }

    private static bool CheckRegistryForNetCoreRuntime(string version, string frameworkName)
    {
        try
        {
            Log($"开始注册表回退检测，目标框架: {frameworkName} {version}");

            // .NET 6+ 安装后会在 Uninstall 中留下记录，DisplayName 包含版本号
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstallKey != null)
            {
                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    if (subKeyName.Contains("Microsoft .NET", StringComparison.OrdinalIgnoreCase))
                    {
                        using var subKey = uninstallKey.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        Log($"检测到注册表项: {subKeyName}, DisplayName={displayName}");
                        if (!string.IsNullOrEmpty(displayName) &&
                            displayName.Contains(version, StringComparison.OrdinalIgnoreCase) &&
                            displayName.Contains(frameworkName == "Microsoft.WindowsDesktop.App" ? "Windows Desktop" : "Microsoft .NET Runtime", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"注册表确认已安装目标运行时 {frameworkName} {version}");
                            return true;
                        }
                    }
                }
            }

            // 备选：dotnet 专用安装记录路径
            string[] regPaths = new[]
            {
                $@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\{frameworkName}",
                frameworkName == "Microsoft.WindowsDesktop.App"
                    ? @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App"
                    : @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"
            };

            foreach (var regPath in regPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        Log($"注册表 {regPath} 值: {valueName}");
                        if (valueName.StartsWith(version, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"注册表确认已安装目标运行时 {frameworkName} {version}");
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"注册表检测异常: {ex.Message}");
        }

        return false;
    }

    // ================================================================
    // 从动态配置的 URL 下载运行时安装包
    // ================================================================
    private static async Task<bool> DownloadRuntimeAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10); // 内网下载给足时间

            using var response = await client.GetAsync(_runtimeUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Log($"HTTP 下载失败: {response.StatusCode}");
                return false;
            }

            await using var fs = new FileStream(TempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);

            Log($"下载完成: {TempInstallerPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"下载异常: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    // 静默安装 .NET 运行时（使用动态 PARAM）
    // ================================================================
    private static void ExecuteSilentInstall()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = TempInstallerPath,
                Arguments = _silentArgs, // 动态使用打包时注入的参数
                Verb = "runas",          // 唤起 UAC 提权
                UseShellExecute = true
            };
            using var process = Process.Start(startInfo);
            process?.WaitForExit();

            Log("安装完成，清理临时文件...");
            try { File.Delete(TempInstallerPath); } catch { }
        }
        catch (Exception ex)
        {
            Log($"安装异常: {ex.Message}");
        }
    }

    // ================================================================
    // 简易日志（写入 Temp 目录，方便排查问题）
    // ================================================================
    private static void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "RuntimeStub.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* 日志写入失败不影响主流程 */ }
    }
}
