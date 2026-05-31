using System.Numerics;

namespace DicomViewer;

internal sealed class VolumeMesh
{
    public required Vector3[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public required Vector3[] SliceCrosshair { get; init; }
    public required Vector3 Center { get; init; }
    public required float Radius { get; init; }
}
