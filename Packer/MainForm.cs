using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Packer;

/// <summary>
/// WinForms UI 打包生成器 v4.0 —— 内置外壳模板、仅保留核心参数。
/// </summary>
public partial class MainForm : Form
{
    private ComboBox cmbTargetVersion = null!;
    private TextBox txtHttpUrl = null!;
    private TextBox txtBusinessExe = null!;
    private TextBox txtOutputDir = null!;
    private Button btnBrowseBusiness = null!;
    private Button btnBrowseOutput = null!;
    private Button btnPack = null!;
    private ProgressBar progressBar = null!;
    private Label lblStatus = null!;

    private static readonly Color ColorBackground = Color.FromArgb(248, 250, 252);
    private static readonly Color ColorCard = Color.White;
    private static readonly Color ColorBorder = Color.FromArgb(226, 232, 240);
    private static readonly Color ColorText = Color.FromArgb(30, 41, 59);
    private static readonly Color ColorTextSecondary = Color.FromArgb(100, 116, 139);
    private static readonly Color ColorPrimary = Color.FromArgb(37, 99, 235);
    private static readonly Color ColorPrimaryHover = Color.FromArgb(29, 78, 216);
    private static readonly Color ColorSuccess = Color.FromArgb(16, 185, 129);

    public MainForm()
    {
        InitializeCustomUI();
    }

    private void InitializeCustomUI()
    {
        this.Text = ".NET 内网独立 EXE 外壳动态打包生成器";
        this.ClientSize = new Size(1100, 750);
        this.MinimumSize = new Size(1100, 750);
        this.MaximizeBox = false;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;

        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = ColorBackground;
        this.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point, 0);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.Padding = new Padding(32);

        // 根布局
        TableLayoutPanel root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = ColorBackground,
            AutoSize = false
        };

        root.RowStyles.Clear();
        // 【修改】主布局第一行改为 AutoSize，确保无论字多大、缩放多少，绝对不会被裁剪
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: 标题区
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // 1: 配置卡片
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));   // 2: 进度状态
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));   // 3: 底部按钮

        // 标题内部布局也必须开启 AutoSize
        TableLayoutPanel headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, // 【修改】改为 Top，配合 AutoSize
            ColumnCount = 1,
            RowCount = 2,
            BackColor = ColorBackground,
            Padding = new Padding(16, 24, 16, 16), // 上下左右预留舒服的边距
            Margin = new Padding(0),
            AutoSize = true,     // 【修改】开启自适应
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        headerLayout.RowStyles.Clear();
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 内部也用 AutoSize
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label lblTitle = new Label
        {
            Text = "动态外壳打包工具",
            Font = new Font("Segoe UI", 22F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ColorText,
            AutoSize = true, // 【修改】改为 true，让控件自己撑开高度
            Margin = new Padding(0, 0, 0, 6) // 用 Margin 来精确控制主副标题的间距
        };

        Label lblSubtitle = new Label
        {
            Text = "为 .NET 业务程序生成内网独立部署 EXE",
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ColorTextSecondary,
            AutoSize = true, // 【修改】改为 true
            Margin = new Padding(0)
        };

        headerLayout.Controls.Add(lblTitle, 0, 0);
        headerLayout.Controls.Add(lblSubtitle, 0, 1);
        root.Controls.Add(headerLayout, 0, 0);


        // 2. 主配置卡片
        Panel cardPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ColorCard,
            Padding = new Padding(32),
            Margin = new Padding(0)
        };
        cardPanel.Paint += CardPanel_Paint;

        TableLayoutPanel formGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            BackColor = ColorCard,
            AutoSize = false
        };
        formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F)); // 标签列
        formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // 输入框列
        formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F)); // 按钮列

        // 宽松行高
        for (int i = 0; i < 5; i++)
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));

        int row = 0;

        // 目标版本
        cmbTargetVersion = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Regular, GraphicsUnit.Point, 0),
            BackColor = ColorCard,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 10, 0, 10)
        };
        cmbTargetVersion.Items.AddRange(new object[] { ".NET 10.0", ".NET 8.0", ".NET 6.0", ".NET Framework 4.8" });
        cmbTargetVersion.SelectedIndex = 0;
        formGrid.Controls.Add(CreateFormLabel("目标版本"), 0, row);
        formGrid.Controls.Add(cmbTargetVersion, 1, row);
        formGrid.SetColumnSpan(cmbTargetVersion, 2);
        row++;

        // 运行时 URL
        txtHttpUrl = CreateStyledTextBox("http://192.168.1.100/dotnet-runtime-10.0.0-win-x64.exe");
        formGrid.Controls.Add(CreateFormLabel("运行时URL"), 0, row);
        formGrid.Controls.Add(txtHttpUrl, 1, row);
        formGrid.SetColumnSpan(txtHttpUrl, 2);
        row++;

        // 业务 EXE
        txtBusinessExe = CreateStyledTextBox("");
        btnBrowseBusiness = CreateBrowseButton("浏览...");
        btnBrowseBusiness.Click += (s, e) => SelectFile("选择业务程序", "可执行文件 (*.exe)|*.exe", txtBusinessExe);
        formGrid.Controls.Add(CreateFormLabel("业务 EXE"), 0, row);
        formGrid.Controls.Add(txtBusinessExe, 1, row);
        formGrid.Controls.Add(btnBrowseBusiness, 2, row);
        row++;

        // 输出目录
        txtOutputDir = CreateStyledTextBox("");
        btnBrowseOutput = CreateBrowseButton("浏览...");
        btnBrowseOutput.Click += (s, e) =>
        {
            using FolderBrowserDialog fbd = new FolderBrowserDialog { Description = "选择打包输出目录" };
            if (fbd.ShowDialog() == DialogResult.OK) txtOutputDir.Text = fbd.SelectedPath;
        };
        formGrid.Controls.Add(CreateFormLabel("输出目录"), 0, row);
        formGrid.Controls.Add(txtOutputDir, 1, row);
        formGrid.Controls.Add(btnBrowseOutput, 2, row);
        row++;

        // 内置外壳提示
        //Label lblEmbeddedHint = new Label
        //{
        //    Text = "外壳模板 RuntimeStub.exe 已内置于打包工具中",
        //    Font = new Font(this.Font.FontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point, 0),
        //    ForeColor = ColorTextSecondary,
        //    AutoSize = false,
        //    Dock = DockStyle.Fill,
        //    TextAlign = ContentAlignment.MiddleLeft
        //};
        //formGrid.Controls.Add(lblEmbeddedHint, 0, row);
        //formGrid.SetColumnSpan(lblEmbeddedHint, 3);

        cardPanel.Controls.Add(formGrid);
        root.Controls.Add(cardPanel, 0, 1);

        // 3. 进度状态区
        Panel statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ColorBackground,
            Padding = new Padding(0, 18, 0, 10)
        };
        progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Style = ProgressBarStyle.Marquee,
            Visible = false,
            Height = 10,
            Margin = new Padding(0, 0, 0, 10)
        };
        lblStatus = new Label
        {
            Text = "就绪。选择业务 EXE 和输出目录后点击「开始打包」。",
            Dock = DockStyle.Bottom,
            Height = 40,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Regular, GraphicsUnit.Point, 0),
            ForeColor = ColorTextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(lblStatus);
        statusPanel.Controls.Add(progressBar);
        root.Controls.Add(statusPanel, 0, 2);

        // 4. 底部打包按钮
        Panel actionPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ColorBackground,
            Padding = new Padding(0, 10, 0, 0)
        };
        btnPack = new Button
        {
            Text = "  开始打包  ",
            Dock = DockStyle.Right,
            Width = 260,
            Height = 58,
            Font = new Font(this.Font.FontFamily, 14F, FontStyle.Bold, GraphicsUnit.Point, 0),
            BackColor = ColorSuccess,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        btnPack.FlatAppearance.BorderSize = 0;
        btnPack.MouseEnter += (s, e) => btnPack.BackColor = Color.FromArgb(5, 150, 105);
        btnPack.MouseLeave += (s, e) => btnPack.BackColor = ColorSuccess;
        btnPack.Click += BtnPack_Click;
        actionPanel.Controls.Add(btnPack);
        root.Controls.Add(actionPanel, 0, 3);

        this.Controls.Add(root);
    }

    private void CardPanel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        using var pen = new Pen(ColorBorder, 1);
        var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = RoundedRect(rect, 12);
        e.Graphics.DrawPath(pen, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var size = new Size(diameter, diameter);
        var arc = new Rectangle(bounds.Location, size);
        var path = new System.Drawing.Drawing2D.GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Label CreateFormLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Regular, GraphicsUnit.Point, 0),
            ForeColor = ColorText,
            AutoSize = false,
            Margin = new Padding(0, 0, 20, 0)
        };
    }

    private TextBox CreateStyledTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Regular, GraphicsUnit.Point, 0),
            BackColor = ColorCard,
            ForeColor = ColorText,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 12, 0, 12),
            Padding = new Padding(10, 0, 10, 0)
        };
    }

    private Button CreateBrowseButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font(this.Font.FontFamily, 11F, FontStyle.Regular, GraphicsUnit.Point, 0),
            BackColor = ColorPrimary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(16, 10, 0, 10)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.MouseEnter += (s, e) => btn.BackColor = ColorPrimaryHover;
        btn.MouseLeave += (s, e) => btn.BackColor = ColorPrimary;
        return btn;
    }

    private static void SelectFile(string title, string filter, TextBox targetTextBox)
    {
        using OpenFileDialog ofd = new OpenFileDialog { Title = title, Filter = filter };
        if (ofd.ShowDialog() == DialogResult.OK) targetTextBox.Text = ofd.FileName;
    }

    private async void BtnPack_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(cmbTargetVersion.Text))
        { MessageBox.Show("请选择目标运行时版本！", "提示"); return; }
        if (string.IsNullOrWhiteSpace(txtHttpUrl.Text) || !txtHttpUrl.Text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        { MessageBox.Show("请输入合法的运行时下载地址！", "提示"); return; }
        if (!File.Exists(txtBusinessExe.Text)) { MessageBox.Show("请选择有效的业务主程序！", "提示"); return; }
        if (!Directory.Exists(txtOutputDir.Text)) { MessageBox.Show("请选择有效的输出目录！", "提示"); return; }

        string versionKey = MapVersionToKey(cmbTargetVersion.SelectedItem?.ToString());
        string runtimeUrl = txtHttpUrl.Text.Trim();

        SetUiState(false);
        lblStatus.Text = "正在进行数据流重组与动态配置注入...";
        lblStatus.ForeColor = ColorPrimary;

        try
        {
            string resultPath = await Task.Run(() =>
            {
                return PackEngine.ExecutePack(
                    txtBusinessExe.Text,
                    txtOutputDir.Text,
                    versionKey,
                    runtimeUrl);
            });

            string runtimeType = PackEngine.DetectRuntimeType(txtBusinessExe.Text);

            lblStatus.Text = "动态外壳生成成功！";
            lblStatus.ForeColor = ColorSuccess;
            MessageBox.Show($"打包成功！\n注入配置：\nVERSION={versionKey}\nRUNTIME={runtimeType}\nURL={runtimeUrl}\n\n输出文件：\n{resultPath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            lblStatus.Text = "打包失败。";
            lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
            MessageBox.Show($"发生错误: {ex.Message}", "错误");
        }
        finally
        {
            SetUiState(true);
        }
    }

    private static string MapVersionToKey(string? selectedVersion)
    {
        return selectedVersion switch
        {
            ".NET 10.0" => "10.0",
            ".NET 8.0" => "8.0",
            ".NET 6.0" => "6.0",
            ".NET Framework 4.8" => "FX48",
            _ => "10.0"
        };
    }

    private void SetUiState(bool enabled)
    {
        cmbTargetVersion.Enabled = enabled;
        txtHttpUrl.Enabled = enabled;
        btnBrowseBusiness.Enabled = enabled;
        btnBrowseOutput.Enabled = enabled;
        txtBusinessExe.Enabled = enabled;
        txtOutputDir.Enabled = enabled;
        btnPack.Enabled = enabled;
        progressBar.Visible = !enabled;
    }

    private void InitializeComponent()
    {

    }
}
