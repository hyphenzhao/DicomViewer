using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class RebuildProgressForm : Form
{
    private readonly Label _messageLabel;
    private readonly ProgressBar _progressBar;

    public RebuildProgressForm()
    {
        Text = "3D 重建";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 110);
        BackColor = Color.FromArgb(37, 37, 38);

        _messageLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(16, 16, 16, 0),
            ForeColor = Color.White,
            Text = "正在准备 3D 重建..."
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 24,
            Margin = new Padding(16),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 16)
        };
        host.Controls.Add(_progressBar);
        host.Controls.Add(_messageLabel);

        Controls.Add(host);
    }

    public void Report(int percent, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Report(percent, message));
            return;
        }

        _progressBar.Value = Math.Clamp(percent, _progressBar.Minimum, _progressBar.Maximum);
        _messageLabel.Text = message;
    }
}
