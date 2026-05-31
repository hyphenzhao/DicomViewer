using System.Drawing;

namespace DicomViewer;

internal sealed class NiftiOverlayLayer
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required float[,,] Voxels { get; init; }
    public required Color Color { get; set; }
    public bool Visible { get; set; } = true;
}
