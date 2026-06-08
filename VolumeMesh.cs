using System.Numerics;
using System.Drawing;

namespace DicomViewer;

internal sealed class VolumeMesh
{
    public required Vector3[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public IReadOnlyList<VolumeMeshPart> Parts { get; init; } = [];
    public bool IsOriginalMri { get; init; }
    public DicomVolume? SourceVolume { get; init; }
    public bool IsVisible { get; set; } = true;
    public required Vector3[] SliceCrosshair { get; init; }
    public required Vector3 Center { get; init; }
    public required float Radius { get; init; }
}

internal sealed class VolumeMeshPart
{
    public required string Name { get; init; }
    public required Vector3[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public required Color Color { get; init; }
    public NiftiOverlayLayer? SourceOverlay { get; init; }
    public bool IsVisible { get; set; } = true;
    public Color DisplayColor => SourceOverlay?.Color ?? Color;
}
