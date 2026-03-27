using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

public sealed class MainForm : Form
{
    private Button _loadButton;
    private Button _loadNiftiButton;
    private Label _statusLabel;
    private readonly TableLayoutPanel _layoutRoot;
    private readonly TableLayoutPanel _viewerGrid;
    private readonly Panel _navPanel;

    private readonly ViewerPane _axialPane;
    private readonly ViewerPane _coronalPane;
    private readonly ViewerPane _sagittalPane;
    private readonly ViewerPane _reconstructionPane;

    private DicomVolume? _currentVolume;
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
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(37, 37, 38)
        };
        _layoutRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _layoutRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _navPanel = BuildNavigationPanel();

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

        _axialPane = new ViewerPane("Axial");
        _coronalPane = new ViewerPane("Coronal");
        _sagittalPane = new ViewerPane("Sagittal");
        _reconstructionPane = new ViewerPane("3D", placeholder: true);
        _reconstructionPane.SetPlaceholder("3D reconstruction placeholder\r\n(coming later)");

        _axialPane.ScrollRequested += (_, delta) => ChangeAxialSlice(delta);
        _coronalPane.ScrollRequested += (_, delta) => ChangeCoronalSlice(delta);
        _sagittalPane.ScrollRequested += (_, delta) => ChangeSagittalSlice(delta);

        _viewerGrid.Controls.Add(_axialPane, 0, 0);
        _viewerGrid.Controls.Add(_coronalPane, 1, 0);
        _viewerGrid.Controls.Add(_sagittalPane, 0, 1);
        _viewerGrid.Controls.Add(_reconstructionPane, 1, 1);

        _layoutRoot.Controls.Add(_navPanel, 0, 0);
        _layoutRoot.Controls.Add(_viewerGrid, 1, 0);

        Controls.Add(_layoutRoot);
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
            RowCount = 6,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            Text = "Navigation",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White
        };

        var subtitle = new Label
        {
            Text = "Load DICOM studies or NIfTI volumes and inspect orthogonal views.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(200, 0),
            Margin = new Padding(0, 0, 0, 12),
            ForeColor = Color.Gainsboro
        };

        _loadButton = new Button
        {
            Text = "Load DICOM Files...",
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
            Text = "Load NIFTI File...",
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

        _statusLabel = new Label
        {
            Text = "No volume loaded",
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
        stack.Controls.Add(_statusLabel, 0, 4);

        panel.Controls.Add(stack);
        return panel;
    }

    private void LoadDicomButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder that contains the DICOM files"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _currentVolume = DicomVolume.LoadFromFolder(dialog.SelectedPath);
            _axialIndex = _currentVolume.AxialIndex;
            _coronalIndex = _currentVolume.CoronalIndex;
            _sagittalIndex = _currentVolume.SagittalIndex;
            RefreshViews();

            _statusLabel.Text = $"Loaded DICOM series from:\r\n{Path.GetFileName(dialog.SelectedPath)}\r\n\r\nFiles loaded: {_currentVolume.SourceFiles.Count}\r\nSlices loaded: {_currentVolume.Depth}\r\nVolume size:\r\n{_currentVolume.Width} × {_currentVolume.Height} × {_currentVolume.Depth}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load DICOM files.\r\n\r\n{ex.Message}", "Load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadNiftiButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select a NIfTI volume",
            Filter = "NIfTI files|*.nii;*.nii.gz|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _currentVolume = DicomVolume.LoadFromNifti(dialog.FileName);
            _axialIndex = _currentVolume.AxialIndex;
            _coronalIndex = _currentVolume.CoronalIndex;
            _sagittalIndex = _currentVolume.SagittalIndex;
            RefreshViews();

            _statusLabel.Text = $"Loaded NIfTI volume from:\r\n{Path.GetFileName(dialog.FileName)}\r\n\r\nFiles loaded: {_currentVolume.SourceFiles.Count}\r\nSlices loaded: {_currentVolume.Depth}\r\nVolume size:\r\n{_currentVolume.Width} × {_currentVolume.Height} × {_currentVolume.Depth}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load NIfTI file.\r\n\r\n{ex.Message}", "Load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshViews()
    {
        if (_currentVolume is null)
        {
            _axialPane.SetImage(null);
            _axialPane.SetLayerPosition(0, 0);
            _coronalPane.SetImage(null);
            _coronalPane.SetLayerPosition(0, 0);
            _sagittalPane.SetImage(null);
            _sagittalPane.SetLayerPosition(0, 0);
            return;
        }

        _axialPane.SetImage(VolumeRenderer.RenderAxial(_currentVolume, _axialIndex));
        _axialPane.SetLayerPosition(_axialIndex, _currentVolume.Depth);
        _coronalPane.SetImage(VolumeRenderer.RenderCoronal(_currentVolume, _coronalIndex));
        _coronalPane.SetLayerPosition(_coronalIndex, _currentVolume.Height);
        _sagittalPane.SetImage(VolumeRenderer.RenderSagittal(_currentVolume, _sagittalIndex));
        _sagittalPane.SetLayerPosition(_sagittalIndex, _currentVolume.Width);
        _reconstructionPane.SetPlaceholder("3D reconstruction placeholder\r\n(coming later)");
    }

    private void ChangeAxialSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Depth == 0)
        {
            return;
        }

        _axialIndex = Math.Clamp(_axialIndex + Math.Sign(delta), 0, _currentVolume.Depth - 1);
        RefreshViews();
    }

    private void ChangeCoronalSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Height == 0)
        {
            return;
        }

        _coronalIndex = Math.Clamp(_coronalIndex + Math.Sign(delta), 0, _currentVolume.Height - 1);
        RefreshViews();
    }

    private void ChangeSagittalSlice(int delta)
    {
        if (_currentVolume is null || _currentVolume.Width == 0)
        {
            return;
        }

        _sagittalIndex = Math.Clamp(_sagittalIndex + Math.Sign(delta), 0, _currentVolume.Width - 1);
        RefreshViews();
    }
}