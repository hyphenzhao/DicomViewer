using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class ViewerPane : UserControl
{
    private readonly Label _titleLabel;
    private readonly Panel _contentPanel;
    private readonly Panel _scrollPanel;
    private readonly PictureBox _pictureBox;
    private readonly Label _placeholderLabel;
    private readonly Panel _layerIndicator;

    private int _currentLayer;
    private int _totalLayers;

    public event EventHandler<int>? ScrollRequested;

    public ViewerPane(string title, bool placeholder = false)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);
        Margin = new Padding(6);

        _titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(8, 6, 8, 0)
        };

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Padding = new Padding(0, 0, 32, 0)
        };

        _scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Black
        };

        _pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.AutoSize,
            Visible = !placeholder
        };

        _placeholderLabel = new Label
        {
            Text = placeholder ? "3D reconstruction placeholder" : string.Empty,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Silver,
            BackColor = Color.FromArgb(20, 20, 20),
            Visible = placeholder
        };

        _layerIndicator = new Panel
        {
            Dock = DockStyle.Right,
            Width = 32,
            BackColor = Color.FromArgb(12, 12, 12)
        };
        _layerIndicator.Paint += LayerIndicator_Paint;

        _scrollPanel.Controls.Add(_pictureBox);
        _scrollPanel.Controls.Add(_placeholderLabel);
        _contentPanel.Controls.Add(_scrollPanel);
        _contentPanel.Controls.Add(_layerIndicator);
        Controls.Add(_contentPanel);
        Controls.Add(_titleLabel);

        MouseWheel += OnMouseWheel;
        _scrollPanel.MouseWheel += OnMouseWheel;
        _pictureBox.MouseWheel += OnMouseWheel;
        _placeholderLabel.MouseWheel += OnMouseWheel;
        _scrollPanel.MouseEnter += (_, _) => _scrollPanel.Focus();
        _pictureBox.MouseEnter += (_, _) => _scrollPanel.Focus();
        _placeholderLabel.MouseEnter += (_, _) => _scrollPanel.Focus();
        _scrollPanel.Resize += (_, _) => CenterDisplayedContent();
    }

    public void SetImage(Image? image)
    {
        _placeholderLabel.Visible = image is null;
        if (image is null)
        {
            _placeholderLabel.Text = "No image loaded";
            _pictureBox.Image = null;
            _pictureBox.Visible = false;
            CenterDisplayedContent();
            return;
        }

        _placeholderLabel.Visible = false;
        _pictureBox.Visible = true;
        _pictureBox.Image = image;
        _pictureBox.Size = image.Size;
        CenterDisplayedContent();
    }

    public void SetPlaceholder(string text)
    {
        _pictureBox.Image = null;
        _pictureBox.Visible = false;
        _placeholderLabel.Text = text;
        _placeholderLabel.Visible = true;
        SetLayerPosition(0, 0);
    }

    public void SetLayerPosition(int currentLayer, int totalLayers)
    {
        _currentLayer = currentLayer;
        _totalLayers = totalLayers;
        _layerIndicator.Invalidate();
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (ScrollRequested is null)
        {
            return;
        }

        ScrollRequested(this, e.Delta > 0 ? 1 : -1);
    }

    private void LayerIndicator_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_layerIndicator.BackColor);

        if (_totalLayers <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int top = 14;
        int bottom = _layerIndicator.Height - 14;
        int centerX = _layerIndicator.Width / 2;
        using var whitePen = new Pen(Color.White, 1f);
        e.Graphics.DrawLine(whitePen, centerX, top, centerX, bottom);

        float ratio = _totalLayers == 1 ? 0f : (float)_currentLayer / (_totalLayers - 1);
        int markerY = top + (int)MathF.Round((bottom - top) * ratio);

        using var bluePen = new Pen(Color.FromArgb(0, 122, 204), 3f);
        e.Graphics.DrawLine(bluePen, centerX - 8, markerY, centerX + 8, markerY);

        string text = (_currentLayer + 1).ToString();
        using var font = new Font("Segoe UI", 8f, FontStyle.Regular);
        Size textSize = TextRenderer.MeasureText(e.Graphics, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int textX = Math.Max(0, centerX - (textSize.Width / 2));
        int textY = Math.Max(0, markerY - textSize.Height - 2);
        TextRenderer.DrawText(e.Graphics, text, font, new Point(textX, textY), Color.White, TextFormatFlags.NoPadding);
    }

    private void CenterDisplayedContent()
    {
        if (_pictureBox.Image is null || !_pictureBox.Visible)
        {
            _pictureBox.Location = Point.Empty;
            return;
        }

        Size viewport = _scrollPanel.ClientSize;
        int x = Math.Max(0, (viewport.Width - _pictureBox.Width) / 2);
        int y = Math.Max(0, (viewport.Height - _pictureBox.Height) / 2);
        _pictureBox.Location = new Point(x, y);
    }
}