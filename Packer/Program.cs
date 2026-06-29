using System;
using System.Windows.Forms;

namespace Packer;

/// <summary>
/// Packer —— WinForms UI 打包生成器入口
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
