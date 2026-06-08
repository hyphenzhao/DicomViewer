using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace DicomViewer;

internal sealed class VolumeRenderControl : Control
{
    private const int MaxRenderedTriangles = 12000;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.4f, -0.6f, -1f));

    private VolumeMesh? _mesh;
    private Color _surfaceColor = Color.FromArgb(90, 180, 255);
    private float _yaw = 0.6f;
    private float _pitch = -0.5f;
    private float _zoom = 1.2f;
    private Point _lastMouse;
    private bool _dragging;
    private int _lastDisplayStride = 1;

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

        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.Clear(Color.Black);

        if (_mesh is null || (_mesh.Parts.Count == 0 && (_mesh.Vertices.Length == 0 || _mesh.Indices.Length < 3)))
        {
            using var brush = new SolidBrush(Color.Silver);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("3D 重建不可用", Font, brush, ClientRectangle, format);
            return;
        }

        Matrix4x4 rotation = Matrix4x4.CreateRotationX(_pitch) * Matrix4x4.CreateRotationY(_yaw);
        var triangles = new List<RenderTriangle>();
        _lastDisplayStride = 1;

        if (_mesh.Parts.Count > 0)
        {
            foreach (VolumeMeshPart part in _mesh.Parts)
            {
                if (!part.IsVisible || part.Vertices.Length == 0 || part.Indices.Length < 3)
                {
                    continue;
                }

                AddTriangles(triangles, part.Vertices, part.Indices, part.DisplayColor, rotation);
            }
        }
        else if (_mesh.IsVisible)
        {
            AddTriangles(triangles, _mesh.Vertices, _mesh.Indices, _surfaceColor, rotation);
        }

        if (triangles.Count == 0)
        {
            using var brush = new SolidBrush(Color.Silver);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("当前 3D 结构已隐藏", Font, brush, ClientRectangle, format);
            return;
        }

        triangles.Sort((left, right) => right.Depth.CompareTo(left.Depth));

        using var brushCache = new BrushCache();
        var points = new PointF[3];
        bool drawEdges = _mesh.Parts.Count == 0;
        using Pen? pen = drawEdges ? new Pen(Color.FromArgb(60, 60, 60), 1f) : null;

        foreach (RenderTriangle triangle in triangles)
        {
            points[0] = triangle.A;
            points[1] = triangle.B;
            points[2] = triangle.C;
            e.Graphics.FillPolygon(brushCache.GetBrush(triangle.Color), points);
            if (pen is not null)
            {
                e.Graphics.DrawPolygon(pen, points);
            }
        }

        if (_lastDisplayStride > 1)
        {
            DrawSamplingNotice(e.Graphics, triangles.Count);
        }

        DrawCrosshair(e.Graphics);
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

    private void AddTriangles(List<RenderTriangle> triangles, Vector3[] vertices, int[] indices, Color baseColor, Matrix4x4 rotation)
    {
        int triangleCount = indices.Length / 3;
        int stride = Math.Max(1, (int)Math.Ceiling(triangleCount / (double)MaxRenderedTriangles));
        _lastDisplayStride = Math.Max(_lastDisplayStride, stride);

        var transformed = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            transformed[i] = Vector3.Transform(vertices[i] - _mesh!.Center, rotation);
        }

        for (int i = 0, triangleIndex = 0; i + 2 < indices.Length; i += 3, triangleIndex++)
        {
            if (triangleIndex % stride != 0)
            {
                continue;
            }

            Vector3 v1 = transformed[indices[i]];
            Vector3 v2 = transformed[indices[i + 1]];
            Vector3 v3 = transformed[indices[i + 2]];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));
            float light = Math.Max(0.45f, Vector3.Dot(normal, LightDirection));
            Color color = ScaleColor(baseColor, light);

            triangles.Add(new RenderTriangle(
                Project(v1),
                Project(v2),
                Project(v3),
                (v1.Z + v2.Z + v3.Z) / 3f,
                color));
        }
    }

    private void DrawSamplingNotice(Graphics graphics, int renderedTriangles)
    {
        string text = $"显示已抽样：{renderedTriangles:N0} 个三角形";
        using var brush = new SolidBrush(Color.FromArgb(190, Color.Gainsboro));
        graphics.DrawString(text, Font, brush, new PointF(8f, 8f));
    }

    private readonly record struct RenderTriangle(PointF A, PointF B, PointF C, float Depth, Color Color);

    private sealed class BrushCache : IDisposable
    {
        private readonly Dictionary<int, SolidBrush> _brushes = [];

        public SolidBrush GetBrush(Color color)
        {
            int argb = color.ToArgb();
            if (!_brushes.TryGetValue(argb, out SolidBrush? brush))
            {
                brush = new SolidBrush(color);
                _brushes.Add(argb, brush);
            }

            return brush;
        }

        public void Dispose()
        {
            foreach (SolidBrush brush in _brushes.Values)
            {
                brush.Dispose();
            }
        }
    }

    private void DrawCrosshair(Graphics graphics)
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
