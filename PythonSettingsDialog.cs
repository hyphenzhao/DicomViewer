using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class PythonSettingsDialog : Form
{
    private readonly PythonSettings _settings;
    private readonly TextBox _pythonPathBox;
    private readonly TextBox _modelsDirBox;
    private readonly TextBox _scriptsDirBox;
    private readonly TextBox _tempDirBox;
    private readonly Button _testButton;
    private readonly Label _testResultLabel;

    public PythonSettingsDialog(PythonSettings settings)
    {
        _settings = settings;

        Text = "设置 — Python 环境";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(560, 380);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(37, 37, 38);

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(16, 16, 16, 12)
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        for (int i = 0; i < 7; i++)
        {
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }

        // Row 0: Python Path
        mainPanel.Controls.Add(HeaderLabel("Python 解释器"), 0, 0);
        _pythonPathBox = new TextBox { Text = settings.PythonInterpreterPath, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        mainPanel.Controls.Add(_pythonPathBox, 1, 0);
        mainPanel.Controls.Add(BrowseButton(() => BrowseFile("选择 Python 解释器", "python.exe|python.exe|所有文件|*.*", _pythonPathBox)), 2, 0);

        // Row 1: Models Directory
        mainPanel.Controls.Add(HeaderLabel("模型目录"), 0, 1);
        _modelsDirBox = new TextBox { Text = settings.ModelsDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        mainPanel.Controls.Add(_modelsDirBox, 1, 1);
        mainPanel.Controls.Add(BrowseButton(() => BrowseFolder("选择模型目录", _modelsDirBox)), 2, 1);

        // Row 2: Scripts Directory
        mainPanel.Controls.Add(HeaderLabel("脚本目录"), 0, 2);
        _scriptsDirBox = new TextBox { Text = settings.ScriptsDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        mainPanel.Controls.Add(_scriptsDirBox, 1, 2);
        mainPanel.Controls.Add(BrowseButton(() => BrowseFolder("选择脚本目录", _scriptsDirBox)), 2, 2);

        // Row 3: Temp Directory
        mainPanel.Controls.Add(HeaderLabel("临时目录"), 0, 3);
        _tempDirBox = new TextBox { Text = settings.TempDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        mainPanel.Controls.Add(_tempDirBox, 1, 3);
        mainPanel.Controls.Add(BrowseButton(() => BrowseFolder("选择临时文件目录", _tempDirBox)), 2, 3);

        // Row 4: Test Connection
        var testPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _testButton = new Button
        {
            Text = "测试连接",
            Width = 110,
            Height = 32,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _testButton.FlatAppearance.BorderSize = 0;
        _testButton.Click += TestButton_Click;
        _testResultLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Silver,
            Padding = new Padding(8, 6, 0, 0)
        };
        testPanel.Controls.Add(_testButton);
        testPanel.Controls.Add(_testResultLabel);
        mainPanel.Controls.Add(testPanel, 1, 4);
        mainPanel.SetColumnSpan(testPanel, 2);

        // Row 5: Buttons (OK/Cancel)
        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 90,
            Height = 36,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(62, 62, 64),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        cancelButton.FlatAppearance.BorderSize = 0;
        var saveButton = new Button
        {
            Text = "保存",
            DialogResult = DialogResult.OK,
            Width = 90,
            Height = 36,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += SaveButton_Click;
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        mainPanel.Controls.Add(buttonPanel, 1, 5);
        mainPanel.SetColumnSpan(buttonPanel, 2);

        Controls.Add(mainPanel);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _settings.PythonInterpreterPath = _pythonPathBox.Text.Trim();
        _settings.ModelsDirectory = _modelsDirBox.Text.Trim();
        _settings.ScriptsDirectory = _scriptsDirBox.Text.Trim();
        _settings.TempDirectory = _tempDirBox.Text.Trim();
        _settings.Save();
    }

    private async void TestButton_Click(object? sender, EventArgs e)
    {
        string pythonPath = _pythonPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pythonPath) || !File.Exists(pythonPath))
        {
            _testResultLabel.Text = "✗ Python 解释器路径无效";
            _testResultLabel.ForeColor = Color.OrangeRed;
            return;
        }

        _testButton.Enabled = false;
        _testResultLabel.Text = "正在测试...";
        _testResultLabel.ForeColor = Color.Silver;

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string version = (output + error).Trim();
            _testResultLabel.Text = process.ExitCode == 0
                ? $"✓ {version}"
                : $"✗ 退出码 {process.ExitCode}: {version}";
            _testResultLabel.ForeColor = process.ExitCode == 0 ? Color.LimeGreen : Color.OrangeRed;
        }
        catch (Exception ex)
        {
            _testResultLabel.Text = $"✗ {ex.Message}";
            _testResultLabel.ForeColor = Color.OrangeRed;
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private static Label HeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gainsboro,
            AutoSize = false
        };
    }

    private static Button BrowseButton(Action browseAction)
    {
        var button = new Button
        {
            Text = "浏览...",
            Width = 90,
            Height = 28,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(62, 62, 64),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => browseAction();
        return button;
    }

    private static void BrowseFile(string title, string filter, TextBox target)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private static void BrowseFolder(string description, TextBox target)
    {
        using var dialog = new FolderBrowserDialog { Description = description };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }
}
