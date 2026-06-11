using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class QuantificationReportForm : Form
{
    public QuantificationReportForm(string jsonReport)
    {
        Text = "膝关节量化分析报告";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(37, 37, 38);

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10f),
            BorderStyle = BorderStyle.None,
            Padding = new Padding(12)
        };

        try
        {
            textBox.Text = FormatReport(jsonReport);
        }
        catch
        {
            textBox.Text = jsonReport;
        }

        var closeButton = new Button
        {
            Text = "关闭",
            Dock = DockStyle.Bottom,
            Height = 42,
            UseVisualStyleBackColor = false,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.Click += (_, _) => Close();

        Controls.Add(textBox);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
    }

    private static string FormatReport(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var lines = new List<string>();
        lines.Add("═══════════════════════════════════════");
        lines.Add("  膝关节软骨形态量化分析报告");
        lines.Add("═══════════════════════════════════════");
        lines.Add("");

        // Voxel info
        if (root.TryGetProperty("voxel_dimensions_mm", out var voxDims))
        {
            lines.Add($"体素尺寸: {string.Join(" × ", voxDims.EnumerateArray().Select(v => $"{v.GetDouble():F3}mm"))}");
        }
        if (root.TryGetProperty("voxel_volume_mm3", out var voxVol))
        {
            lines.Add($"体素体积: {voxVol.GetDouble():F4} mm³");
        }
        lines.Add("");

        // Volume analysis
        if (root.TryGetProperty("volume_analysis", out var volAnalysis))
        {
            lines.Add("─── 体积分析 ───");
            foreach (var prop in volAnalysis.EnumerateObject())
            {
                var v = prop.Value;
                double volMl = v.TryGetProperty("volume_ml", out var ml) ? ml.GetDouble() : 0;
                int voxCnt = v.TryGetProperty("voxel_count", out var vc) ? vc.GetInt32() : 0;
                lines.Add($"  {prop.Name}:");
                lines.Add($"    体积: {volMl:F4} ml ({voxCnt:N0} 体素)");
            }
            lines.Add("");
        }

        // Surface area
        if (root.TryGetProperty("surface_area_analysis", out var surfAnalysis))
        {
            lines.Add("─── 表面积分析 ───");
            foreach (var prop in surfAnalysis.EnumerateObject())
            {
                var v = prop.Value;
                double area = v.TryGetProperty("surface_area_mm2", out var a) ? a.GetDouble() : 0;
                string method = v.TryGetProperty("method", out var m) ? $" [{m.GetString()}]" : "";
                lines.Add($"  {prop.Name}: {area:F2} mm²{method}");
            }
            lines.Add("");
        }

        // Thickness analysis
        if (root.TryGetProperty("thickness_analysis", out var thickAnalysis))
        {
            lines.Add("─── 厚度分析 ───");
            foreach (var prop in thickAnalysis.EnumerateObject())
            {
                var v = prop.Value;
                double mean = v.TryGetProperty("mean_mm", out var mn) ? mn.GetDouble() : 0;
                double std = v.TryGetProperty("std_mm", out var sd) ? sd.GetDouble() : 0;
                double max = v.TryGetProperty("max_mm", out var mx) ? mx.GetDouble() : 0;
                double p95 = v.TryGetProperty("percentile_95_mm", out var p) ? p.GetDouble() : 0;
                lines.Add($"  {prop.Name}:");
                lines.Add($"    均值: {mean:F3}mm  |  标准差: {std:F3}mm  |  最大: {max:F3}mm  |  P95: {p95:F3}mm");
            }
            lines.Add("");
        }

        // Total
        if (root.TryGetProperty("cartilage_total_volume_ml", out var totalVol))
        {
            lines.Add($"软骨总体积: {totalVol.GetDouble():F4} ml");
        }

        lines.Add("");
        lines.Add("═══════════════════════════════════════");

        return string.Join(Environment.NewLine, lines);
    }
}
