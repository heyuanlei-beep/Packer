using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Packer;

/// <summary>
/// 通过 Win32 API 直接操作 PE 资源段，将源 EXE 的图标完美克隆到目标 EXE。
/// 无需第三方库，纯 P/Invoke 实现。
/// </summary>
public static class IconChanger
{
    // ================================================================
    // Win32 P/Invoke 声明
    // ================================================================

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResource(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    // ================================================================
    // 常量
    // ================================================================

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    private static readonly IntPtr RT_ICON = (IntPtr)3;
    private static readonly IntPtr RT_GROUP_ICON = (IntPtr)14;
    private const ushort LANG_NEUTRAL = 0;

    // ================================================================
    // 数据结构
    // ================================================================

    private class IconResource
    {
        public IntPtr Name;
        public byte[] Data = null!;
    }

    // ================================================================
    // 核心方法：将源 EXE 的图标资源完美写入到目标 EXE 中
    // ================================================================

    /// <summary>
    /// 从 sourceExe 提取所有图标资源（RT_GROUP_ICON + RT_ICON），
    /// 然后写入到 targetExe 的 PE 资源段中。
    /// </summary>
    /// <param name="sourceExe">源 EXE 路径（通常为业务 EXE，提供图标）</param>
    /// <param name="targetExe">目标 EXE 路径（通常为外壳输出文件）</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    public static bool CloneIcon(string sourceExe, string targetExe)
    {
        List<IconResource> groupIcons = new();
        List<IconResource> icons = new();

        // ---- 1. 加载源 EXE，枚举并提取图标资源 ----
        IntPtr hModule = LoadLibraryEx(sourceExe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (hModule == IntPtr.Zero)
        {
            Console.WriteLine($"[IconChanger] 无法加载源文件: {sourceExe} (Error: {Marshal.GetLastWin32Error()})");
            return false;
        }

        try
        {
            EnumResourceNames(hModule, RT_GROUP_ICON,
                (mod, type, name, _) => CollectResource(mod, type, name, groupIcons),
                IntPtr.Zero);

            EnumResourceNames(hModule, RT_ICON,
                (mod, type, name, _) => CollectResource(mod, type, name, icons),
                IntPtr.Zero);
        }
        finally
        {
            FreeLibrary(hModule);
        }

        if (groupIcons.Count == 0)
        {
            Console.WriteLine("[IconChanger] 源 EXE 中未找到图标资源，跳过图标克隆。");
            return false;
        }

        Console.WriteLine($"[IconChanger] 提取到 {groupIcons.Count} 个图标组, {icons.Count} 个图标资源。");

        // ---- 2. 将提取的图标资源写入目标 EXE ----
        IntPtr hUpdate = BeginUpdateResource(targetExe, false);
        if (hUpdate == IntPtr.Zero)
        {
            Console.WriteLine($"[IconChanger] BeginUpdateResource 失败 (Error: {Marshal.GetLastWin32Error()})");
            return false;
        }

        bool success = true;

        // 写入图标组
        foreach (var group in groupIcons)
        {
            if (!UpdateResource(hUpdate, RT_GROUP_ICON, group.Name, LANG_NEUTRAL, group.Data, (uint)group.Data.Length))
            {
                Console.WriteLine($"[IconChanger] 更新 RT_GROUP_ICON 失败 (Error: {Marshal.GetLastWin32Error()})");
                success = false;
            }
        }

        // 写入各个图标
        foreach (var icon in icons)
        {
            if (!UpdateResource(hUpdate, RT_ICON, icon.Name, LANG_NEUTRAL, icon.Data, (uint)icon.Data.Length))
            {
                Console.WriteLine($"[IconChanger] 更新 RT_ICON 失败 (Error: {Marshal.GetLastWin32Error()})");
                success = false;
            }
        }

        // 提交更新
        if (!EndUpdateResource(hUpdate, !success))
        {
            Console.WriteLine($"[IconChanger] EndUpdateResource 失败 (Error: {Marshal.GetLastWin32Error()})");
            success = false;
        }

        if (success)
            Console.WriteLine("[IconChanger] 图标克隆完成。");

        return success;
    }

    /// <summary>
    /// EnumResourceNames 回调：读取单个资源的数据并添加到列表
    /// </summary>
    private static bool CollectResource(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, List<IconResource> collection)
    {
        IntPtr hResInfo = FindResource(hModule, lpszName, lpszType);
        if (hResInfo == IntPtr.Zero) return true;

        IntPtr hResData = LoadResource(hModule, hResInfo);
        if (hResData == IntPtr.Zero) return true;

        IntPtr lpData = LockResource(hResData);
        uint size = SizeofResource(hModule, hResInfo);
        if (lpData == IntPtr.Zero || size == 0) return true;

        byte[] data = new byte[size];
        Marshal.Copy(lpData, data, 0, (int)size);

        collection.Add(new IconResource { Name = lpszName, Data = data });
        return true; // 继续枚举
    }
}
