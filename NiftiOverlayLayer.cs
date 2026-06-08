using System.Drawing;

namespace DicomViewer;

internal sealed class NiftiOverlayLayer
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required float[,,] Voxels { get; init; }
    public required Color Color { get; set; }
    public OverlayKind Kind { get; set; } = OverlayKind.Unknown;
    public string AnatomyStructure { get; set; } = "其它";
    public bool CanBuild3D { get; set; } = true;
    public float? LabelValue { get; set; }
    public bool Visible { get; set; } = true;
}

internal enum OverlayKind
{
    Unknown,
    Mask,
    LabelMap,
    Thickness,
    Layering,
    Morphology
}
