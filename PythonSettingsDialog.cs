using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class PythonSettingsDialog : Form
{
    private readonly PythonSettings _settings;
    private readonly ComboBox _modeComboBox;
    private readonly Panel _localPanel;
    private readonly Panel _remotePanel;
    private TextBox _pythonPathBox;
    private TextBox _modelsDirBox;
    private TextBox _scriptsDirBox;
    private TextBox _tempDirBox;
    private TextBox _serverUrlBox;
    private TextBox _apiKeyBox;
    private readonly Button _testButton;
    private readonly Label _testResultLabel;

    public PythonSettingsDialog(PythonSettings settings)
    {
        _settings = settings;

        Text = "设置 — AI 分割";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(560, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(37, 37, 38);

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(16, 16, 16, 12)
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        for (int i = 0; i < 8; i++)
        {
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }

        // Row 0: Mode selector
        mainPanel.Controls.Add(HeaderLabel("推理模式"), 0, 0);
        _modeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _modeComboBox.Items.AddRange(["本地推理", "远程服务器"]);
        _modeComboBox.SelectedIndex = settings.SegmentationMode == "Remote" ? 1 : 0;
        _modeComboBox.SelectedIndexChanged += (_, _) => UpdateModeVisibility();
        mainPanel.Controls.Add(_modeComboBox, 1, 0);
        mainPanel.SetColumnSpan(_modeComboBox, 2);

        // Row 1-4: Local mode panel (hosted in rows 1-4)
        _localPanel = BuildLocalPanel();
        mainPanel.Controls.Add(_localPanel, 0, 1);
        mainPanel.SetColumnSpan(_localPanel, 3);
        mainPanel.SetRowSpan(_localPanel, 4);

        // Row 1-3: Remote mode panel (hosted in same rows, hidden by default)
        _remotePanel = BuildRemotePanel();
        mainPanel.Controls.Add(_remotePanel, 0, 1);
        mainPanel.SetColumnSpan(_remotePanel, 3);
        mainPanel.SetRowSpan(_remotePanel, 3);

        // Row 5: Test connection (remote) or spacer
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
        mainPanel.Controls.Add(testPanel, 1, 5);
        mainPanel.SetColumnSpan(testPanel, 2);

        // Row 6: Buttons (OK/Cancel)
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
        mainPanel.Controls.Add(buttonPanel, 1, 6);
        mainPanel.SetColumnSpan(buttonPanel, 2);

        Controls.Add(mainPanel);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        UpdateModeVisibility();
    }

    /// <summary>
    /// Builds the panel shown when "本地推理" is selected.
    /// </summary>
    private Panel BuildLocalPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        for (int i = 0; i < 4; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }

        // Row 0: Python Path
        panel.Controls.Add(HeaderLabel("Python 解释器"), 0, 0);
        _pythonPathBox = new TextBox { Text = _settings.PythonInterpreterPath, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(_pythonPathBox, 1, 0);
        panel.Controls.Add(BrowseButton(() => BrowseFile("选择 Python 解释器", "python.exe|python.exe|所有文件|*.*", _pythonPathBox)), 2, 0);

        // Row 1: Models Directory
        panel.Controls.Add(HeaderLabel("模型目录"), 0, 1);
        _modelsDirBox = new TextBox { Text = _settings.ModelsDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(_modelsDirBox, 1, 1);
        panel.Controls.Add(BrowseButton(() => BrowseFolder("选择模型目录", _modelsDirBox)), 2, 1);

        // Row 2: Scripts Directory
        panel.Controls.Add(HeaderLabel("脚本目录"), 0, 2);
        _scriptsDirBox = new TextBox { Text = _settings.ScriptsDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(_scriptsDirBox, 1, 2);
        panel.Controls.Add(BrowseButton(() => BrowseFolder("选择脚本目录", _scriptsDirBox)), 2, 2);

        // Row 3: Temp Directory
        panel.Controls.Add(HeaderLabel("临时目录"), 0, 3);
        _tempDirBox = new TextBox { Text = _settings.TempDirectory, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(_tempDirBox, 1, 3);
        panel.Controls.Add(BrowseButton(() => BrowseFolder("选择临时文件目录", _tempDirBox)), 2, 3);

        return panel;
    }

    /// <summary>
    /// Builds the panel shown when "远程服务器" is selected.
    /// </summary>
    private Panel BuildRemotePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        // Row 0: Server URL
        panel.Controls.Add(HeaderLabel("服务器地址"), 0, 0);
        _serverUrlBox = new TextBox
        {
            Text = _settings.RemoteServerUrl,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "http://192.168.1.100:8000"
        };
        panel.Controls.Add(_serverUrlBox, 1, 0);
        panel.SetColumnSpan(_serverUrlBox, 2);

        // Row 1: API Key
        panel.Controls.Add(HeaderLabel("API Key"), 0, 1);
        _apiKeyBox = new TextBox
        {
            Text = _settings.RemoteApiKey,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = true,
            PlaceholderText = "与服务器共享的密钥"
        };
        panel.Controls.Add(_apiKeyBox, 1, 1);
        panel.SetColumnSpan(_apiKeyBox, 2);

        return panel;
    }

    private void UpdateModeVisibility()
    {
        bool isRemote = _modeComboBox.SelectedIndex == 1;
        _localPanel.Visible = !isRemote;
        _remotePanel.Visible = isRemote;
        _testButton.Visible = isRemote;
        _testResultLabel.Visible = isRemote;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        bool isRemote = _modeComboBox.SelectedIndex == 1;
        _settings.SegmentationMode = isRemote ? "Remote" : "Local";
        _settings.PythonInterpreterPath = _pythonPathBox.Text.Trim();
        _settings.ModelsDirectory = _modelsDirBox.Text.Trim();
        _settings.ScriptsDirectory = _scriptsDirBox.Text.Trim();
        _settings.TempDirectory = _tempDirBox.Text.Trim();
        _settings.RemoteServerUrl = _serverUrlBox.Text.Trim();
        _settings.RemoteApiKey = _apiKeyBox.Text.Trim();
        _settings.Save();
    }

    private async void TestButton_Click(object? sender, EventArgs e)
    {
        string serverUrl = _serverUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            _testResultLabel.Text = "✗ 请输入服务器地址";
            _testResultLabel.ForeColor = Color.OrangeRed;
            return;
        }

        _testButton.Enabled = false;
        _testResultLabel.Text = "正在测试...";
        _testResultLabel.ForeColor = Color.Silver;

        try
        {
            using var client = new RemoteSegmentationClient();
            string result = await client.TestConnectionAsync(serverUrl);
            _testResultLabel.Text = $"✓ 连接成功";
            _testResultLabel.ForeColor = Color.LimeGreen;

            // Try to parse health check response
            try
            {
                var health = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result);
                if (health.TryGetProperty("model_loaded", out var modelLoaded) && modelLoaded.GetBoolean())
                {
                    _testResultLabel.Text = "✓ 连接成功，模型已加载";
                }
                else
                {
                    _testResultLabel.Text = "⚠ 连接成功，但模型未加载（检查服务器 CMT_MODEL_DIR）";
                    _testResultLabel.ForeColor = Color.Gold;
                }
            }
            catch
            {
                // Ignore parse errors — connection succeeded
            }
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
