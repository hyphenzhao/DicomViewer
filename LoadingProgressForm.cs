using System.Drawing;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class LoadingProgressForm : Form
{
    private readonly Label _totalLabel;
    private readonly ProgressBar _totalProgressBar;
    private readonly Label _currentFileLabel;
    private readonly ProgressBar _currentFileProgressBar;

    public LoadingProgressForm()
    {
        Text = "正在加载数据";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 170);
        BackColor = Color.FromArgb(37, 37, 38);

        _totalLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Color.White,
            Text = "正在准备加载..."
        };

        _totalProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 22,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _currentFileLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(16, 12, 16, 0),
            ForeColor = Color.Gainsboro,
            Text = "等待读取文件..."
        };

        _currentFileProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 22,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };
        host.Controls.Add(_currentFileProgressBar);
        host.Controls.Add(_currentFileLabel);
        host.Controls.Add(_totalProgressBar);
        host.Controls.Add(_totalLabel);

        Controls.Add(host);
    }

    public void Report(LoadProgress progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Report(progress));
            return;
        }

        _totalProgressBar.Value = Math.Clamp(progress.TotalPercent, _totalProgressBar.Minimum, _totalProgressBar.Maximum);
        _currentFileProgressBar.Value = Math.Clamp(progress.CurrentFilePercent, _currentFileProgressBar.Minimum, _currentFileProgressBar.Maximum);
        _totalLabel.Text = progress.TotalMessage;
        _currentFileLabel.Text = progress.CurrentFileMessage;
        _currentFileLabel.Visible = progress.ShowCurrentFileProgress;
        _currentFileProgressBar.Visible = progress.ShowCurrentFileProgress;
    }
}
