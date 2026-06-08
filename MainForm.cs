using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DicomViewer;

public sealed class MainForm : Form
{
    private Button _loadButton;
    private Button _loadNiftiButton;
    private Button _loadNiftiFolderButton;
    private Button _rebuildOriginalMriButton;
    private Button _rebuildSegmentedMriButton;
    private ComboBox _presetComboBox;
    private TrackBar _thresholdTrackBar;
    private Label _thresholdLabel;
    private NumericUpDown _smoothingInput;
    private Label _statusLabel;
    private FlowLayoutPanel _overlayPanel;
    private FlowLayoutPanel _meshVisibilityPanel;
    private Panel _reconstructionHostPanel;
    private CheckBox _showOriginal2dCheckBox;
    private CheckBox _useOriginal3dCheckBox;
    private readonly TableLayoutPanel _layoutRoot;
    private readonly TableLayoutPanel _viewerGrid;
    private readonly Panel _navPanel;
    private readonly Panel _overlayWorkspacePanel;

    private readonly ViewerPane _axialPane;
    private readonly ViewerPane _coronalPane;
    private readonly ViewerPane _sagittalPane;
    private readonly ViewerPane _reconstructionPane;
    private readonly VolumeRenderControl _volumeRenderControl;

    private DicomVolume? _currentVolume;
    private VolumeMesh? _currentMesh;
    private RebuildSettings _rebuildSettings = new() { Preset = VolumePreset.CtBone, ThresholdRatio = 0.55f, SmoothingPasses = 1 };
    private int _axialIndex;
    private int _coronalIndex;
    private int _sagittalIndex;

    public MainForm()
    {
        Text = "DicomViewer";
        MinimumSize = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(37, 37, 38);

        _layoutRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.FromArgb(37, 37, 38)
        };
        _layoutRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _layoutRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layoutRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

        _navPanel = BuildNavigationPanel();
        _overlayWorkspacePanel = BuildOverlayWorkspacePanel();

        _viewerGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(37, 37, 38)
        };
        _viewerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _viewerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _viewerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _viewerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _axialPane = new ViewerPane("轴位");
        _coronalPane = new ViewerPane("冠状位");
        _sagittalPane = new ViewerPane("矢状位");
        _reconstructionPane = new ViewerPane("3D", placeholder: true);
        _reconstructionPane.SetPlaceholder("3D 重建占位图\r\n（稍后可用）");
        _volumeRenderControl = new VolumeRenderControl();
        _reconstructionHostPanel = BuildReconstructionHostPanel();

        _axialPane.ScrollRequested += (_, delta) => ChangeAxialSlice(delta);
        _coronalPane.ScrollRequested += (_, delta) => ChangeCoronalSlice(delta);
        _sagittalPane.ScrollRequested += (_, delta) => ChangeSagittalSlice(delta);
        _axialPane.ImageClicked += (_, point) => ChangeLinkedSlicesFromAxial(point);
        _coronalPane.ImageClicked += (_, point) => ChangeLinkedSlicesFromCoronal(point);
        _sagittalPane.ImageClicked += (_, point) => ChangeLinkedSlicesFromSagittal(point);

        _viewerGrid.Controls.Add(_axialPane, 0, 0);
        _viewerGrid.Controls.Add(_coronalPane, 1, 0);
        _viewerGrid.Controls.Add(_sagittalPane, 0, 1);
        _viewerGrid.Controls.Add(_reconstructionHostPanel, 1, 1);

        _layoutRoot.Controls.Add(_navPanel, 0, 0);
        _layoutRoot.Controls.Add(_viewerGrid, 1, 0);
        _layoutRoot.Controls.Add(_overlayWorkspacePanel, 2, 0);

        Controls.Add(_layoutRoot);
        RefreshOriginalMriControls();
    }

    private Panel BuildNavigationPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(12)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "导航",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White
        };

        var subtitle = new Label
        {
            Text = "加载 DICOM 检查或 NIfTI 体数据，并查看正交视图。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(200, 0),
            Margin = new Padding(0, 0, 0, 12),
            ForeColor = Color.Gainsboro
        };

        _loadButton = new Button
        {
            Text = "加载 DICOM 文件...",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Width = 200,
            MinimumSize = new Size(200, 42),
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _loadButton.FlatAppearance.BorderSize = 0;
        _loadButton.Click += LoadDicomButton_Click;

        _loadNiftiButton = new Button
        {
            Text = "加载 NIfTI 文件...",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Width = 200,
            MinimumSize = new Size(200, 42),
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _loadNiftiButton.FlatAppearance.BorderSize = 0;
        _loadNiftiButton.Click += LoadNiftiButton_Click;

        _loadNiftiFolderButton = new Button
        {
            Text = "加载 NIfTI 文件夹...",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Width = 200,
            MinimumSize = new Size(200, 42),
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _loadNiftiFolderButton.FlatAppearance.BorderSize = 0;
        _loadNiftiFolderButton.Click += LoadNiftiFolderButton_Click;

        _rebuildOriginalMriButton = new Button
        {
            Text = "原始 MRI 重建",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Width = 200,
            MinimumSize = new Size(200, 42),
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _rebuildOriginalMriButton.FlatAppearance.BorderSize = 0;
        _rebuildOriginalMriButton.Click += RebuildOriginalMriButton_Click;

        _rebuildSegmentedMriButton = new Button
        {
            Text = "已分割 MRI 重建",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Width = 200,
            MinimumSize = new Size(200, 42),
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _rebuildSegmentedMriButton.FlatAppearance.BorderSize = 0;
        _rebuildSegmentedMriButton.Click += RebuildSegmentedMriButton_Click;

        var presetLabel = new Label
        {
            Text = "3D 预设",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.Gainsboro
        };

        _presetComboBox = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 12)
        };
        _presetComboBox.Items.AddRange(["CT 骨骼", "CT 软组织", "MRI 脑部", "自定义"]);
        _presetComboBox.SelectedIndex = 0;
        _presetComboBox.SelectedIndexChanged += (_, _) => ApplyPresetSelection();

        _thresholdLabel = new Label
        {
            Text = "阈值：55%",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.Gainsboro
        };

        _thresholdTrackBar = new TrackBar
        {
            Dock = DockStyle.Top,
            Minimum = 5,
            Maximum = 95,
            TickFrequency = 10,
            Value = 55,
            Margin = new Padding(0, 0, 0, 12)
        };
        _thresholdTrackBar.ValueChanged += (_, _) =>
        {
            _thresholdLabel.Text = $"阈值：{_thresholdTrackBar.Value}%";
            _rebuildSettings = _rebuildSettings with { ThresholdRatio = _thresholdTrackBar.Value / 100f, Preset = VolumePreset.Custom };
            _presetComboBox.SelectedIndex = 3;
        };

        var smoothingLabel = new Label
        {
            Text = "平滑次数",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.Gainsboro
        };

        _smoothingInput = new NumericUpDown
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 5,
            Value = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        _smoothingInput.ValueChanged += (_, _) => _rebuildSettings = _rebuildSettings with { SmoothingPasses = (int)_smoothingInput.Value };

        _statusLabel = new Label
        {
            Text = "未加载体数据",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(200, 0),
            Margin = new Padding(0),
            ForeColor = Color.Silver,
            Padding = new Padding(0, 4, 0, 0)
        };

        stack.Controls.Add(title, 0, 0);
        stack.Controls.Add(subtitle, 0, 1);
        stack.Controls.Add(_loadButton, 0, 2);
        stack.Controls.Add(_loadNiftiButton, 0, 3);
        stack.Controls.Add(_loadNiftiFolderButton, 0, 4);
        stack.Controls.Add(_rebuildOriginalMriButton, 0, 5);
        stack.Controls.Add(_rebuildSegmentedMriButton, 0, 6);
        stack.Controls.Add(presetLabel, 0, 7);
        stack.Controls.Add(_presetComboBox, 0, 8);
        stack.Controls.Add(_thresholdLabel, 0, 9);
        stack.Controls.Add(_thresholdTrackBar, 0, 10);
        stack.Controls.Add(smoothingLabel, 0, 11);
        stack.Controls.Add(_smoothingInput, 0, 12);
        stack.Controls.Add(_statusLabel, 0, 13);

        panel.Controls.Add(stack);
        return panel;
    }

    private Panel BuildOverlayWorkspacePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(12)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            Text = "叠加层工作区",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White
        };

        var subtitle = new Label
        {
            Text = "为分割层、掩膜或厚度图选择 2D 显示颜色。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(220, 0),
            Margin = new Padding(0, 0, 0, 12),
            ForeColor = Color.Gainsboro
        };

        _overlayPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };

        stack.Controls.Add(title, 0, 0);
        stack.Controls.Add(subtitle, 0, 1);
        stack.Controls.Add(BuildOriginalMriControlPanel(), 0, 2);
        stack.Controls.Add(_overlayPanel, 0, 3);
        panel.Controls.Add(stack);
        return panel;
    }

    private Panel BuildReconstructionHostPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            BackColor = Color.FromArgb(30, 30, 30)
        };

        _reconstructionPane.Dock = DockStyle.Fill;
        _reconstructionPane.Margin = new Padding(0);

        _meshVisibilityPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 6, 6, 0),
            BackColor = Color.FromArgb(45, 45, 48),
            Visible = false
        };

        panel.Controls.Add(_reconstructionPane);
        panel.Controls.Add(_meshVisibilityPanel);
        return panel;
    }

    private Panel BuildOriginalMriControlPanel()
    {
        var panel = new Panel
        {
            Width = 220,
            Height = 82,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.FromArgb(37, 37, 38)
        };

        var title = new Label
        {
            Text = "原始 MRI",
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(6, 6),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        _showOriginal2dCheckBox = new CheckBox
        {
            Text = "2D 显示",
            ForeColor = Color.Gainsboro,
            Checked = true,
            Width = 90,
            Location = new Point(6, 32)
        };
        _showOriginal2dCheckBox.CheckedChanged += (_, _) =>
        {
            if (_currentVolume is null)
            {
                return;
            }

            _currentVolume.ShowOriginalIn2D = _showOriginal2dCheckBox.Checked;
            RefreshViews();
        };

        _useOriginal3dCheckBox = new CheckBox
        {
            Text = "参与 3D",
            ForeColor = Color.Gainsboro,
            Checked = true,
            Width = 90,
            Location = new Point(6, 56)
        };
        _useOriginal3dCheckBox.CheckedChanged += (_, _) =>
        {
            if (_currentVolume is null)
            {
                return;
            }

            _currentVolume.UseOriginalFor3D = _useOriginal3dCheckBox.Checked;
            SetRebuildButtonsEnabled(true);
            _volumeRenderControl.Invalidate();
        };

        panel.Controls.Add(title);
        panel.Controls.Add(_showOriginal2dCheckBox);
        panel.Controls.Add(_useOriginal3dCheckBox);
        return panel;
    }

    private async void LoadDicomButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 DICOM 文件的文件夹"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _currentVolume = await LoadVolumeWithProgressAsync(progress => DicomVolume.LoadFromFolder(dialog.SelectedPath, progress));
            _currentMesh = null;
            RefreshMeshVisibilityPanel();
            _axialIndex = _currentVolume.AxialIndex;
            _coronalIndex = _currentVolume.CoronalIndex;
            _sagittalIndex = _currentVolume.SagittalIndex;
            RefreshOriginalMriControls();
            RefreshOverlayPanel();
            RefreshViews();
            SetRebuildButtonsEnabled(true);

            _statusLabel.Text = $"已从以下位置加载 DICOM 序列：\r\n{Path.GetFileName(dialog.SelectedPath)}\r\n\r\n已加载文件：{_currentVolume.SourceFiles.Count}\r\n已加载切片：{_currentVolume.Depth}\r\n体数据大小：\r\n{_currentVolume.Width} × {_currentVolume.Height} × {_currentVolume.Depth}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"加载 DICOM 文件失败。\r\n\r\n{ex.Message}", "加载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void LoadNiftiButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 NIfTI 体数据",
            Filter = "NIfTI 文件|*.nii;*.nii.gz|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _currentVolume = await LoadVolumeWithProgressAsync(progress => DicomVolume.LoadFromNifti(dialog.FileName, progress));
            _currentMesh = null;
            RefreshMeshVisibilityPanel();
            _axialIndex = _currentVolume.AxialIndex;
            _coronalIndex = _currentVolume.CoronalIndex;
            _sagittalIndex = _currentVolume.SagittalIndex;
            RefreshOriginalMriControls();
            RefreshOverlayPanel();
            RefreshViews();
            SetRebuildButtonsEnabled(true);

            _statusLabel.Text = $"已从以下位置加载 NIfTI 体数据：\r\n{Path.GetFileName(dialog.FileName)}\r\n\r\n已加载文件：{_currentVolume.SourceFiles.Count}\r\n已加载切片：{_currentVolume.Depth}\r\n体数据大小：\r\n{_currentVolume.Width} × {_currentVolume.Height} × {_currentVolume.Depth}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"加载 NIfTI 文件失败。\r\n\r\n{ex.Message}", "加载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void LoadNiftiFolderButton_Click(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择包含原始图像和分割层的 NIfTI 文件夹"
        };

        if (folderDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        IReadOnlyList<string> niftiFiles = DicomVolume.DiscoverNiftiFiles(folderDialog.SelectedPath);
        if (niftiFiles.Count == 0)
        {
            MessageBox.Show(this, "所选文件夹中没有找到 NIfTI 文件。", "加载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string? primaryPath = SelectPrimaryNifti(niftiFiles);
        if (primaryPath is null)
        {
            return;
        }

        try
        {
            _currentVolume = await LoadVolumeWithProgressAsync(progress => DicomVolume.LoadFromNiftiFolder(folderDialog.SelectedPath, primaryPath, progress));
            _currentMesh = null;
            RefreshMeshVisibilityPanel();
            _axialIndex = _currentVolume.AxialIndex;
            _coronalIndex = _currentVolume.CoronalIndex;
            _sagittalIndex = _currentVolume.SagittalIndex;
            RefreshOriginalMriControls();
            RefreshOverlayPanel();
            RefreshViews();
            SetRebuildButtonsEnabled(true);

            _statusLabel.Text = $"已加载原始 NIfTI：\r\n{Path.GetFileName(primaryPath)}\r\n\r\n叠加层：{_currentVolume.Overlays.Count}\r\n切片：{_currentVolume.Depth}\r\n体数据大小：\r\n{_currentVolume.Width} × {_currentVolume.Height} × {_currentVolume.Depth}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"加载 NIfTI 文件夹失败。\r\n\r\n{ex.Message}", "加载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string? SelectPrimaryNifti(IReadOnlyList<string> niftiFiles)
    {
        using var dialog = new Form
        {
            Text = "选择原始 NIfTI 图像",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(520, 360),
            MinimizeBox = false,
            MaximizeBox = false
        };

        var label = new Label
        {
            Text = "请选择作为原始 MRI 图像显示的 NIfTI 文件：",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(12, 12, 12, 0)
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true
        };
        foreach (string path in niftiFiles)
        {
            listBox.Items.Add(Path.GetFileName(path));
        }
        listBox.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46,
            Padding = new Padding(8)
        };
        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 90 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        dialog.Controls.Add(listBox);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK && listBox.SelectedIndex >= 0
            ? niftiFiles[listBox.SelectedIndex]
            : null;
    }

    private async Task<DicomVolume> LoadVolumeWithProgressAsync(Func<IProgress<LoadProgress>, DicomVolume> loadFunc)
    {
        SetLoadButtonsEnabled(false);
        using var progressForm = new LoadingProgressForm();
        var progress = new Progress<LoadProgress>(progressForm.Report);
        progressForm.Show(this);
        progressForm.BringToFront();

        try
        {
            return await Task.Run(() => loadFunc(progress));
        }
        finally
        {
            progressForm.Close();
            SetLoadButtonsEnabled(true);
        }
    }

    private void SetLoadButtonsEnabled(bool enabled)
    {
        _loadButton.Enabled = enabled;
        _loadNiftiButton.Enabled = enabled;
        _loadNiftiFolderButton.Enabled = enabled;
    }

    private void SetRebuildButtonsEnabled(bool enabled)
    {
        _rebuildOriginalMriButton.Enabled = enabled && _currentVolume?.UseOriginalFor3D == true;
        _rebuildSegmentedMriButton.Enabled = enabled && _currentVolume?.Overlays.Any(overlay => overlay.Visible && overlay.CanBuild3D) == true;
    }

    private void RefreshOriginalMriControls()
    {
        bool hasVolume = _currentVolume is not null;
        _showOriginal2dCheckBox.Enabled = hasVolume;
        _useOriginal3dCheckBox.Enabled = hasVolume;
        _showOriginal2dCheckBox.Checked = _currentVolume?.ShowOriginalIn2D ?? true;
        _useOriginal3dCheckBox.Checked = _currentVolume?.UseOriginalFor3D ?? true;
    }

    private void RefreshOverlayPanel()
    {
        _overlayPanel.Controls.Clear();

        if (_currentVolume is null || _currentVolume.Overlays.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "无叠加层",
                AutoSize = true,
                ForeColor = Color.Silver,
                Margin = new Padding(0, 0, 0, 6)
            };
            _overlayPanel.Controls.Add(emptyLabel);
            return;
        }

        foreach (NiftiOverlayLayer overlay in _currentVolume.Overlays)
        {
            var row = new Panel
            {
                Width = 220,
                Height = 112,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.FromArgb(37, 37, 38)
            };

            var visibleCheckBox = new CheckBox
            {
                Checked = overlay.Visible,
                ForeColor = Color.Gainsboro,
                Text = overlay.Name,
                AutoEllipsis = true,
                Width = 170,
                Location = new Point(6, 6)
            };
            visibleCheckBox.CheckedChanged += (_, _) =>
            {
                overlay.Visible = visibleCheckBox.Checked;
                SetRebuildButtonsEnabled(_currentVolume is not null);
                RefreshViews();
                _volumeRenderControl.Invalidate();
            };

            var colorButton = new Button
            {
                BackColor = overlay.Color,
                FlatStyle = FlatStyle.Flat,
                Width = 34,
                Height = 24,
                Location = new Point(180, 6),
                Text = string.Empty
            };
            colorButton.FlatAppearance.BorderColor = Color.White;
            colorButton.Click += (_, _) => ChooseOverlayColor(overlay, colorButton);

            var kindLabel = new Label
            {
                Text = $"类型：{GetOverlayKindDisplayName(overlay.Kind)}",
                ForeColor = Color.Silver,
                AutoSize = true,
                Location = new Point(6, 34)
            };

            var structureComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 142,
                Location = new Point(6, 58)
            };
            structureComboBox.Items.AddRange(GetAnatomyStructureOptions());
            structureComboBox.SelectedItem = structureComboBox.Items.Contains(overlay.AnatomyStructure) ? overlay.AnatomyStructure : "其它";
            structureComboBox.SelectedIndexChanged += (_, _) =>
            {
                overlay.AnatomyStructure = structureComboBox.SelectedItem?.ToString() ?? "其它";
            };

            var build3dCheckBox = new CheckBox
            {
                Checked = overlay.CanBuild3D,
                ForeColor = Color.Gainsboro,
                Text = "参与 3D",
                Width = 80,
                Location = new Point(6, 84)
            };
            build3dCheckBox.CheckedChanged += (_, _) =>
            {
                overlay.CanBuild3D = build3dCheckBox.Checked;
                SetRebuildButtonsEnabled(_currentVolume is not null);
                _volumeRenderControl.Invalidate();
            };

            row.Controls.Add(visibleCheckBox);
            row.Controls.Add(colorButton);
            row.Controls.Add(kindLabel);
            row.Controls.Add(structureComboBox);
            row.Controls.Add(build3dCheckBox);
            _overlayPanel.Controls.Add(row);
        }
    }

    private static object[] GetAnatomyStructureOptions()
    {
        return
        [
            "其它",
            "股骨",
            "胫骨",
            "髌骨",
            "股骨软骨",
            "胫骨软骨",
            "髌骨软骨",
            "软骨",
            "内侧半月板",
            "外侧半月板",
            "半月板",
            "ACL 前交叉韧带",
            "PCL 后交叉韧带",
            "MCL 内侧副韧带",
            "LCL 外侧副韧带"
        ];
    }

    private static string GetOverlayKindDisplayName(OverlayKind kind)
    {
        return kind switch
        {
            OverlayKind.Mask => "Mask",
            OverlayKind.LabelMap => "LabelMap",
            OverlayKind.Thickness => "Thickness",
            OverlayKind.Layering => "Layering",
            OverlayKind.Morphology => "Morphology",
            _ => "Unknown"
        };
    }

    private void ChooseOverlayColor(NiftiOverlayLayer overlay, Button colorButton)
    {
        using var dialog = new ColorDialog
        {
            Color = overlay.Color,
            FullOpen = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        overlay.Color = dialog.Color;
        colorButton.BackColor = dialog.Color;
        RefreshViews();
        _volumeRenderControl.Invalidate();
    }

    private void RefreshMeshVisibilityPanel()
    {
        _meshVisibilityPanel.Controls.Clear();

        if (_currentMesh is null)
        {
            _meshVisibilityPanel.Visible = false;
            return;
        }

        if (_currentMesh.Parts.Count == 0)
        {
            AddMeshVisibilityCheckBox("原始 MRI", _currentMesh.IsVisible, value => _currentMesh.IsVisible = value);
        }
        else
        {
            foreach (VolumeMeshPart part in _currentMesh.Parts)
            {
                string name = string.IsNullOrWhiteSpace(part.Name) ? part.SourceOverlay?.Name ?? "结构" : part.Name;
                AddMeshVisibilityCheckBox(name, part.IsVisible, value => part.IsVisible = value);
            }
        }

        _meshVisibilityPanel.Visible = _meshVisibilityPanel.Controls.Count > 0;
    }

    private void AddMeshVisibilityCheckBox(string text, bool isChecked, Action<bool> onChanged)
    {
        var checkBox = new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(0, 0, 12, 0)
        };
        checkBox.CheckedChanged += (_, _) =>
        {
            onChanged(checkBox.Checked);
            _volumeRenderControl.Invalidate();
        };

        _meshVisibilityPanel.Controls.Add(checkBox);
    }

    private void RefreshViews()
    {
        if (_currentVolume is null)
        {
            SetRebuildButtonsEnabled(false);
            _axialPane.SetImage(null);
            _axialPane.SetCrosshairPosition(null);
            _axialPane.SetLayerPosition(0, 0);
            _coronalPane.SetImage(null);
            _coronalPane.SetCrosshairPosition(null);
            _coronalPane.SetLayerPosition(0, 0);
            _sagittalPane.SetImage(null);
            _sagittalPane.SetCrosshairPosition(null);
            _sagittalPane.SetLayerPosition(0, 0);
            _reconstructionPane.SetHostedContent(null);
            RefreshMeshVisibilityPanel();
            return;
        }

        _axialPane.SetImage(VolumeRenderer.RenderAxial(_currentVolume, _axialIndex));
        _axialPane.SetCrosshairPosition(new Point(_sagittalIndex, GetAxialDisplayYFromVolumeY(_coronalIndex)));
        _axialPane.SetLayerPosition(_axialIndex, _currentVolume.Depth);
        _coronalPane.SetImage(VolumeRenderer.RenderCoronal(_currentVolume, _coronalIndex));
        _coronalPane.SetCrosshairPosition(new Point(_sagittalIndex, GetCoronalDisplayYFromVolumeZ(_axialIndex)));
        _coronalPane.SetLayerPosition(_coronalIndex, _currentVolume.Height);
        _sagittalPane.SetImage(VolumeRenderer.RenderSagittal(_currentVolume, _sagittalIndex));
        _sagittalPane.SetCrosshairPosition(new Point(GetSagittalDisplayXFromVolumeY(_coronalIndex), GetSagittalDisplayYFromVolumeZ(_axialIndex)));
        _sagittalPane.SetLayerPosition(_sagittalIndex, _currentVolume.Width);
        if (_currentMesh is null)
        {
            _reconstructionPane.SetHostedContent(null);
            _meshVisibilityPanel.Visible = false;
            _reconstructionPane.SetPlaceholder("3D 重建占位图\r\n选择“原始 MRI 重建”或“已分割 MRI 重建”生成。");
        }
        else
        {
            _volumeRenderControl.SetMesh(_currentMesh);
            _reconstructionPane.SetHostedContent(_volumeRenderControl);
        }
    }

    private async void RebuildOriginalMriButton_Click(object? sender, EventArgs e)
    {
        if (_currentVolume is null)
        {
            return;
        }

        if (!_currentVolume.UseOriginalFor3D)
        {
            MessageBox.Show(this, "原始 MRI 当前未勾选“参与 3D”。", "3D 重建", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetRebuildButtonsEnabled(false);
        _loadButton.Enabled = false;
        _loadNiftiButton.Enabled = false;
        _loadNiftiFolderButton.Enabled = false;

        using var progressForm = new RebuildProgressForm();
        progressForm.Show(this);
        progressForm.BringToFront();

        var progress = new Progress<(int Percent, string Message)>(state => progressForm.Report(state.Percent, state.Message));

        try
        {
            _currentMesh = await Task.Run(() => VolumeRebuilder.RebuildOriginalMri(_currentVolume, _rebuildSettings, _axialIndex, _coronalIndex, _sagittalIndex, progress));
            _volumeRenderControl.SetMesh(_currentMesh);
            _volumeRenderControl.SetPreset(_rebuildSettings.Preset);
            _reconstructionPane.SetHostedContent(_volumeRenderControl);
            RefreshMeshVisibilityPanel();
            _statusLabel.Text += "\r\n\r\n原始 MRI 重建已就绪。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"原始 MRI 重建失败。\r\n\r\n{ex.Message}", "3D 重建错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            progressForm.Close();
            _loadButton.Enabled = true;
            _loadNiftiButton.Enabled = true;
            _loadNiftiFolderButton.Enabled = true;
            SetRebuildButtonsEnabled(_currentVolume is not null);
            RefreshViews();
        }
    }

    private async void RebuildSegmentedMriButton_Click(object? sender, EventArgs e)
    {
        if (_currentVolume is null)
        {
            return;
        }

        SetRebuildButtonsEnabled(false);
        _loadButton.Enabled = false;
        _loadNiftiButton.Enabled = false;
        _loadNiftiFolderButton.Enabled = false;

        using var progressForm = new RebuildProgressForm();
        progressForm.Show(this);
        progressForm.BringToFront();

        var progress = new Progress<(int Percent, string Message)>(state => progressForm.Report(state.Percent, state.Message));

        try
        {
            _currentMesh = await Task.Run(() => VolumeRebuilder.RebuildSegmentedMri(_currentVolume, _currentVolume.Overlays.Where(overlay => overlay.CanBuild3D), _axialIndex, _coronalIndex, _sagittalIndex, progress));
            _volumeRenderControl.SetMesh(_currentMesh);
            _reconstructionPane.SetHostedContent(_volumeRenderControl);
            RefreshMeshVisibilityPanel();
            _statusLabel.Text += "\r\n\r\n已分割 MRI 重建已就绪。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"已分割 MRI 重建失败。\r\n\r\n{ex.Message}", "3D 重建错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            progressForm.Close();
            _loadButton.Enabled = true;
            _loadNiftiButton.Enabled = true;
            _loadNiftiFolderButton.Enabled = true;
            SetRebuildButtonsEnabled(_currentVolume is not null);
            RefreshViews();
        }
    }

    private void ChangeAxialSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Depth == 0)
        {
            return;
        }

        _axialIndex = Math.Clamp(_axialIndex + Math.Sign(delta), 0, _currentVolume.Depth - 1);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ChangeCoronalSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Height == 0)
        {
            return;
        }

        _coronalIndex = Math.Clamp(_coronalIndex + Math.Sign(delta), 0, _currentVolume.Height - 1);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ChangeSagittalSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Width == 0)
        {
            return;
        }

        _sagittalIndex = Math.Clamp(_sagittalIndex + Math.Sign(delta), 0, _currentVolume.Width - 1);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ChangeLinkedSlicesFromAxial(Point point)
    {
        if (_currentVolume is null)
        {
            return;
        }

        _sagittalIndex = Math.Clamp(point.X, 0, _currentVolume.Width - 1);
        _coronalIndex = GetVolumeYFromAxialDisplay(point.Y);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ChangeLinkedSlicesFromCoronal(Point point)
    {
        if (_currentVolume is null)
        {
            return;
        }

        _sagittalIndex = Math.Clamp(point.X, 0, _currentVolume.Width - 1);
        _axialIndex = GetVolumeZFromCoronalDisplay(point.Y);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ChangeLinkedSlicesFromSagittal(Point point)
    {
        if (_currentVolume is null)
        {
            return;
        }

        _coronalIndex = GetVolumeYFromSagittalDisplay(point.X);
        _axialIndex = GetVolumeZFromSagittalDisplay(point.Y);
        UpdateCrosshair();
        RefreshViews();
    }

    private void ApplyPresetSelection()
    {
        _rebuildSettings = _presetComboBox.SelectedIndex switch
        {
            0 => new RebuildSettings { Preset = VolumePreset.CtBone, ThresholdRatio = 0.72f, SmoothingPasses = 2 },
            1 => new RebuildSettings { Preset = VolumePreset.CtSoftTissue, ThresholdRatio = 0.48f, SmoothingPasses = 2 },
            2 => new RebuildSettings { Preset = VolumePreset.MriBrain, ThresholdRatio = 0.38f, SmoothingPasses = 3 },
            _ => new RebuildSettings { Preset = VolumePreset.Custom, ThresholdRatio = _thresholdTrackBar.Value / 100f, SmoothingPasses = (int)_smoothingInput.Value }
        };

        _thresholdTrackBar.Value = (int)Math.Round(_rebuildSettings.ThresholdRatio * 100f);
        _thresholdLabel.Text = $"阈值：{_thresholdTrackBar.Value}%";
        _smoothingInput.Value = _rebuildSettings.SmoothingPasses;
        _volumeRenderControl.SetPreset(_rebuildSettings.Preset);
    }

    private int GetVolumeYFromAxialDisplay(int displayY)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int y = ClampIndex(displayY, _currentVolume.Height);
        return _currentVolume.Orientation.FlipAxialVertical ? _currentVolume.Height - 1 - y : y;
    }

    private int GetAxialDisplayYFromVolumeY(int volumeY)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int y = ClampIndex(volumeY, _currentVolume.Height);
        return _currentVolume.Orientation.FlipAxialVertical ? _currentVolume.Height - 1 - y : y;
    }

    private int GetVolumeZFromCoronalDisplay(int displayY)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int z = ClampIndex(displayY, _currentVolume.Depth);
        return _currentVolume.Orientation.FlipCoronalVertical ? _currentVolume.Depth - 1 - z : z;
    }

    private int GetCoronalDisplayYFromVolumeZ(int volumeZ)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int z = ClampIndex(volumeZ, _currentVolume.Depth);
        return _currentVolume.Orientation.FlipCoronalVertical ? _currentVolume.Depth - 1 - z : z;
    }

    private int GetVolumeYFromSagittalDisplay(int displayX)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int y = ClampIndex(displayX, _currentVolume.Height);
        return _currentVolume.Orientation.FlipSagittalHorizontal ? _currentVolume.Height - 1 - y : y;
    }

    private int GetSagittalDisplayXFromVolumeY(int volumeY)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int y = ClampIndex(volumeY, _currentVolume.Height);
        return _currentVolume.Orientation.FlipSagittalHorizontal ? _currentVolume.Height - 1 - y : y;
    }

    private int GetVolumeZFromSagittalDisplay(int displayY)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int z = ClampIndex(displayY, _currentVolume.Depth);
        return _currentVolume.Orientation.FlipSagittalVertical ? _currentVolume.Depth - 1 - z : z;
    }

    private int GetSagittalDisplayYFromVolumeZ(int volumeZ)
    {
        if (_currentVolume is null)
        {
            return 0;
        }

        int z = ClampIndex(volumeZ, _currentVolume.Depth);
        return _currentVolume.Orientation.FlipSagittalVertical ? _currentVolume.Depth - 1 - z : z;
    }

    private static int ClampIndex(int value, int length)
    {
        return Math.Clamp(value, 0, Math.Max(0, length - 1));
    }

    private void UpdateCrosshair()
    {
        if (_currentVolume is null || _currentMesh is null)
        {
            return;
        }

        _currentMesh = new VolumeMesh
        {
            Vertices = _currentMesh.Vertices,
            Indices = _currentMesh.Indices,
            Parts = _currentMesh.Parts,
            IsOriginalMri = _currentMesh.IsOriginalMri,
            SourceVolume = _currentMesh.SourceVolume,
            SliceCrosshair =
            [
                new System.Numerics.Vector3(-1f, ((Math.Clamp(_coronalIndex, 0, Math.Max(0, _currentVolume.Height - 1)) / Math.Max(1f, _currentVolume.Height - 1f)) - 0.5f) * -2f, ((Math.Clamp(_axialIndex, 0, Math.Max(0, _currentVolume.Depth - 1)) / Math.Max(1f, _currentVolume.Depth - 1f)) - 0.5f) * 2f),
                new System.Numerics.Vector3(1f, ((Math.Clamp(_coronalIndex, 0, Math.Max(0, _currentVolume.Height - 1)) / Math.Max(1f, _currentVolume.Height - 1f)) - 0.5f) * -2f, ((Math.Clamp(_axialIndex, 0, Math.Max(0, _currentVolume.Depth - 1)) / Math.Max(1f, _currentVolume.Depth - 1f)) - 0.5f) * 2f),
                new System.Numerics.Vector3(((Math.Clamp(_sagittalIndex, 0, Math.Max(0, _currentVolume.Width - 1)) / Math.Max(1f, _currentVolume.Width - 1f)) - 0.5f) * 2f, -1f, ((Math.Clamp(_axialIndex, 0, Math.Max(0, _currentVolume.Depth - 1)) / Math.Max(1f, _currentVolume.Depth - 1f)) - 0.5f) * 2f),
                new System.Numerics.Vector3(((Math.Clamp(_sagittalIndex, 0, Math.Max(0, _currentVolume.Width - 1)) / Math.Max(1f, _currentVolume.Width - 1f)) - 0.5f) * 2f, 1f, ((Math.Clamp(_axialIndex, 0, Math.Max(0, _currentVolume.Depth - 1)) / Math.Max(1f, _currentVolume.Depth - 1f)) - 0.5f) * 2f),
                new System.Numerics.Vector3(((Math.Clamp(_sagittalIndex, 0, Math.Max(0, _currentVolume.Width - 1)) / Math.Max(1f, _currentVolume.Width - 1f)) - 0.5f) * 2f, ((Math.Clamp(_coronalIndex, 0, Math.Max(0, _currentVolume.Height - 1)) / Math.Max(1f, _currentVolume.Height - 1f)) - 0.5f) * -2f, -1f),
                new System.Numerics.Vector3(((Math.Clamp(_sagittalIndex, 0, Math.Max(0, _currentVolume.Width - 1)) / Math.Max(1f, _currentVolume.Width - 1f)) - 0.5f) * 2f, ((Math.Clamp(_coronalIndex, 0, Math.Max(0, _currentVolume.Height - 1)) / Math.Max(1f, _currentVolume.Height - 1f)) - 0.5f) * -2f, 1f)
            ],
            Center = _currentMesh.Center,
            Radius = _currentMesh.Radius
        };

        _volumeRenderControl.SetMesh(_currentMesh);
    }
}