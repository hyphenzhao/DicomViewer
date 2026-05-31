using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class VolumeRenderControl : Control
{
    private VolumeMesh? _mesh;
    private Color _surfaceColor = Color.FromArgb(90, 180, 255);
    private float _yaw = 0.6f;
    private float _pitch = -0.5f;
    private float _zoom = 1.2f;
    private Point _lastMouse;
    private bool _dragging;

    public VolumeRenderControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetMesh(VolumeMesh? mesh)
    {
        _mesh = mesh;
        Invalidate();
    }

    public void SetPreset(VolumePreset preset)
    {
        _surfaceColor = preset switch
        {
            VolumePreset.CtBone => Color.FromArgb(245, 245, 220),
            VolumePreset.CtSoftTissue => Color.FromArgb(205, 160, 140),
            VolumePreset.MriBrain => Color.FromArgb(160, 200, 255),
            _ => Color.FromArgb(90, 180, 255)
        };

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.Black);

        if (_mesh is null || _mesh.Vertices.Length == 0 || _mesh.Indices.Length < 3)
        {
            using var brush = new SolidBrush(Color.Silver);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("3D 重建不可用", Font, brush, ClientRectangle, format);
            return;
        }

        Vector3[] transformed = new Vector3[_mesh.Vertices.Length];
        Matrix4x4 rotation = Matrix4x4.CreateRotationX(_pitch) * Matrix4x4.CreateRotationY(_yaw);
        for (int i = 0; i < _mesh.Vertices.Length; i++)
        {
            transformed[i] = Vector3.Transform(_mesh.Vertices[i] - _mesh.Center, rotation);
        }

        var triangles = new List<(PointF[] Points, float Depth, Color Color)>();
        for (int i = 0; i + 2 < _mesh.Indices.Length; i += 3)
        {
            Vector3 v1 = transformed[_mesh.Indices[i]];
            Vector3 v2 = transformed[_mesh.Indices[i + 1]];
            Vector3 v3 = transformed[_mesh.Indices[i + 2]];

            Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));
            float light = Math.Max(0.12f, Vector3.Dot(normal, Vector3.Normalize(new Vector3(0.4f, -0.6f, -1f))));
            Color color = ScaleColor(_surfaceColor, light);

            triangles.Add((
                [Project(v1), Project(v2), Project(v3)],
                (v1.Z + v2.Z + v3.Z) / 3f,
                color));
        }

        foreach (var triangle in triangles.OrderByDescending(t => t.Depth))
        {
            using var brush = new SolidBrush(triangle.Color);
            using var pen = new Pen(Color.FromArgb(24, 24, 24), 1f);
            e.Graphics.FillPolygon(brush, triangle.Points);
            e.Graphics.DrawPolygon(pen, triangle.Points);
        }

        DrawCrosshair(e.Graphics, transformed);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _lastMouse = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        Point delta = new(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
        _yaw += delta.X * 0.01f;
        _pitch += delta.Y * 0.01f;
        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.1f : -0.1f), 0.4f, 3.5f);
        Invalidate();
    }

    private PointF Project(Vector3 point)
    {
        float depth = point.Z + 3.2f;
        float perspective = 1.9f / Math.Max(0.3f, depth);
        float scale = Math.Min(ClientSize.Width, ClientSize.Height) * 0.42f * _zoom;
        float x = (point.X * perspective * scale) + (ClientSize.Width / 2f);
        float y = (point.Y * perspective * scale) + (ClientSize.Height / 2f);
        return new PointF(x, y);
    }

    private static Color ScaleColor(Color baseColor, float amount)
    {
        int r = Math.Clamp((int)(baseColor.R * amount), 0, 255);
        int g = Math.Clamp((int)(baseColor.G * amount), 0, 255);
        int b = Math.Clamp((int)(baseColor.B * amount), 0, 255);
        return Color.FromArgb(r, g, b);
    }

    private void DrawCrosshair(Graphics graphics, Vector3[] transformedVertices)
    {
        if (_mesh?.SliceCrosshair is null || _mesh.SliceCrosshair.Length < 6)
        {
            return;
        }

        Matrix4x4 rotation = Matrix4x4.CreateRotationX(_pitch) * Matrix4x4.CreateRotationY(_yaw);
        using var pen = new Pen(Color.OrangeRed, 2f);

        for (int i = 0; i + 1 < _mesh.SliceCrosshair.Length; i += 2)
        {
            Vector3 start = Vector3.Transform(_mesh.SliceCrosshair[i] - _mesh.Center, rotation);
            Vector3 end = Vector3.Transform(_mesh.SliceCrosshair[i + 1] - _mesh.Center, rotation);
            graphics.DrawLine(pen, Project(start), Project(end));
        }
    }
}
