using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Packer;

/// <summary>
/// 核心打包引擎 —— 数据追加法（Overlay）v4.0：内置外壳、默认静默参数。
///
/// 尾部数据布局：
///   [内置外壳母体原始数据（含克隆后的图标）] [业务 EXE 二进制数据] [512 字节 KEY=VALUE 配置区] [业务 EXE 长度 (8 bytes, Int64 LE)]
///
/// 配置区示例：
///   VERSION=10.0;PARAM=/passive /norestart;URL=http://192.168.1.100/dotnet-runtime-10.0.0-win-x64.exe
///
/// ⚠️ 关键：必须先克隆图标，再追加业务数据！
///   因为 UpdateResource 会重写整个 PE 文件结构，如果先追加数据再改图标，
///   追加的数据会被 PE 重写过程覆盖丢失。
///
/// 外壳启动时：
///   1. 从文件末尾倒数 520 字节处读取 512 字节 → 解析为 KEY=VALUE 配置
///   2. 读取文件最后 8 字节 → 得到 payloadSize
///   3. 从文件末尾向前偏移 (8 + 512 + payloadSize) → 得到业务数据起点
///   4. 读取 payloadSize 字节 → 写入 .real.exe → 启动
/// </summary>
public static class PackEngine
{
    public const int ConfigBufferSize = 512;
    public const int TrailerSize = 8;

    /// <summary>
    /// 打包生成最终可执行文件。
    /// </summary>
    /// <param name="sourceBusinessExe">原本开发好的 .NET 业务单文件</param>
    /// <param name="outputDirectory">生成的目标文件夹</param>
    /// <param name="versionKey">目标运行时版本（如 "10.0" / "FX48"）</param>
    /// <param name="runtimeUrl">内网 HTTP 运行时下载地址</param>
    /// <returns>最终生成的文件完整路径</returns>
    public static string ExecutePack(
        string sourceBusinessExe,
        string outputDirectory,
        string versionKey,
        string runtimeUrl)
    {
        if (!File.Exists(sourceBusinessExe))
            throw new FileNotFoundException("业务程序不存在", sourceBusinessExe);

        string silentArgs = versionKey switch
        {
            "FX48" => "/q /norestart",
            _ => "/passive /norestart"
        };

        // 自动探测业务程序需要 .NET Runtime 还是 .NET Desktop Runtime
        string runtimeType = DetectRuntimeType(sourceBusinessExe);
        Console.WriteLine($"  运行时类型: {runtimeType}");

        byte[] stubBytes = GetEmbeddedStubBytes();

        Directory.CreateDirectory(outputDirectory);

        string exeName = Path.GetFileName(sourceBusinessExe);
        string finalOutputPath = Path.Combine(outputDirectory, exeName);

        Console.WriteLine($"[PackEngine] 开始打包...");
        Console.WriteLine($"  业务程序: {sourceBusinessExe}");
        Console.WriteLine($"  输出路径: {finalOutputPath}");
        Console.WriteLine($"  目标版本: {versionKey}");
        Console.WriteLine($"  静默参数: {silentArgs}");
        Console.WriteLine($"  运行时URL: {runtimeUrl}");
        Console.WriteLine($"  运行时类型: {runtimeType}");
        Console.WriteLine($"  内置外壳大小: {FormatSize(stubBytes.Length)}");

        // 1. 将内嵌外壳母体写入输出文件
        File.WriteAllBytes(finalOutputPath, stubBytes);
        long stubSize = stubBytes.Length;

        // 2. 【跳过图标克隆】
        // 当前外壳为 PublishSingleFile=true 生成的单文件 apphost，UpdateResource 重写 PE 结构
        // 会破坏其内部 bundle，导致外壳自身无法启动。业务程序图标保留在原始业务 EXE 上即可。
        Console.WriteLine("[PackEngine] 图标克隆已禁用（单文件外壳不支持资源重写）");

        // 3. 读取业务 EXE 的全部二进制数据
        byte[] businessBytes = File.ReadAllBytes(sourceBusinessExe);
        long businessSize = businessBytes.Length;
        Console.WriteLine($"  业务程序大小: {FormatSize(businessSize)}");

        // 4. 准备 512 字节的配置区数据
        string configString = $"VERSION={versionKey};PARAM={silentArgs};URL={runtimeUrl};RUNTIME={runtimeType}";
        byte[] urlBytes = Encoding.UTF8.GetBytes(configString);
        if (urlBytes.Length > ConfigBufferSize)
        {
            throw new ArgumentException($"配置字符串编码后长度 ({urlBytes.Length} 字节) 超过配置区上限 ({ConfigBufferSize} 字节)");
        }
        byte[] configBuffer = new byte[ConfigBufferSize];
        Array.Copy(urlBytes, configBuffer, urlBytes.Length);

        // 5. 【后追加业务数据】将业务数据 + 配置区 + 长度标识追加到文件尾部
        using (FileStream fs = new(finalOutputPath, FileMode.Append, FileAccess.Write))
        {
            fs.Write(businessBytes, 0, businessBytes.Length);
            fs.Write(configBuffer, 0, configBuffer.Length);
            byte[] sizeBytes = BitConverter.GetBytes(businessSize);
            fs.Write(sizeBytes, 0, sizeBytes.Length);
        }

        long finalSize = new FileInfo(finalOutputPath).Length;
        Console.WriteLine($"  最终文件大小: {FormatSize(finalSize)}");
        Console.WriteLine($"[PackEngine] 打包完成! → {finalOutputPath}");
        return finalOutputPath;
    }

    /// <summary>
    /// 从当前程序集的内嵌资源中读取 RuntimeStub.exe 的字节数组。
    /// </summary>
    private static byte[] GetEmbeddedStubBytes()
    {
        Assembly assembly = typeof(PackEngine).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("RuntimeStub.exe");
        if (stream == null)
        {
            throw new InvalidOperationException(
                "内嵌的外壳模板 RuntimeStub.exe 不存在。" +
                "请确认 Packer.csproj 已正确配置 EmbeddedResource，并已成功构建 RuntimeStub 项目。");
        }

        using MemoryStream ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    /// <summary>
    /// 根据业务 EXE 的 runtimeconfig.json 自动判断需要 .NET Runtime 还是 .NET Desktop Runtime。
    /// 如果业务程序依赖 WindowsDesktop，则返回 desktop；否则返回 core。
    /// 找不到配置文件时默认按 desktop 处理（多数内网业务程序为 WinForms/WPF）。
    /// </summary>
    public static string DetectRuntimeType(string businessExePath)
    {
        try
        {
            string configPath = Path.ChangeExtension(businessExePath, ".runtimeconfig.json");
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[PackEngine] 未找到 {Path.GetFileName(configPath)}，默认按 .NET Desktop Runtime 处理。");
                return "desktop";
            }

            string json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("runtimeOptions", out var runtimeOptions))
            {
                if (runtimeOptions.TryGetProperty("framework", out var framework))
                {
                    if (framework.TryGetProperty("name", out var name) &&
                        name.GetString()?.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return "desktop";
                    }
                    return "core";
                }

                if (runtimeOptions.TryGetProperty("includedFrameworks", out var includedFrameworks) &&
                    includedFrameworks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fw in includedFrameworks.EnumerateArray())
                    {
                        if (fw.TryGetProperty("name", out var fwName) &&
                            fwName.GetString()?.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return "desktop";
                        }
                    }
                    return "core";
                }
            }

            Console.WriteLine("[PackEngine] runtimeconfig.json 中未明确识别框架类型，默认按 .NET Desktop Runtime 处理。");
            return "desktop";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PackEngine] 解析 runtimeconfig.json 失败: {ex.Message}，默认按 .NET Desktop Runtime 处理。");
            return "desktop";
        }
    }
}
