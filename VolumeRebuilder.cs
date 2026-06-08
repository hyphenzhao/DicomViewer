using System.Numerics;

namespace DicomViewer;

internal static class VolumeRebuilder
{
    public static VolumeMesh RebuildOriginalMri(DicomVolume volume, RebuildSettings settings, int axialIndex, int coronalIndex, int sagittalIndex, IProgress<(int Percent, string Message)>? progress = null)
    {
        return Rebuild(volume, settings, axialIndex, coronalIndex, sagittalIndex, progress);
    }

    public static VolumeMesh RebuildSegmentedMri(DicomVolume volume, IEnumerable<NiftiOverlayLayer> overlays, int axialIndex, int coronalIndex, int sagittalIndex, IProgress<(int Percent, string Message)>? progress = null)
    {
        var visibleOverlays = overlays.Where(overlay => overlay.Visible && overlay.CanBuild3D).ToList();
        if (visibleOverlays.Count == 0)
        {
            throw new InvalidOperationException("没有可用于已分割 MRI 重建的可见 3D 叠加层。");
        }

        var parts = new List<VolumeMeshPart>();
        Vector3 sum = Vector3.Zero;
        int vertexCount = 0;
        float maxDistanceSquared = 1f;

        for (int i = 0; i < visibleOverlays.Count; i++)
        {
            NiftiOverlayLayer overlay = visibleOverlays[i];
            progress?.Report(((int)Math.Round((i / Math.Max(1d, visibleOverlays.Count)) * 90d), $"正在重建 {overlay.AnatomyStructure}（{overlay.Name}）..."));
            VolumeMeshPart part = RebuildMaskPart(volume, overlay);
            if (part.Vertices.Length == 0 || part.Indices.Length < 3)
            {
                continue;
            }

            parts.Add(part);
            foreach (Vector3 vertex in part.Vertices)
            {
                sum += vertex;
                vertexCount++;
            }
        }

        if (parts.Count == 0 || vertexCount == 0)
        {
            throw new InvalidOperationException("可见叠加层中没有可重建的非零体素。");
        }

        Vector3 center = sum / vertexCount;
        foreach (VolumeMeshPart part in parts)
        {
            foreach (Vector3 vertex in part.Vertices)
            {
                maxDistanceSquared = Math.Max(maxDistanceSquared, Vector3.DistanceSquared(center, vertex));
            }
        }

        progress?.Report((100, "已分割 MRI 重建已就绪。"));
        return new VolumeMesh
        {
            Vertices = parts.SelectMany(part => part.Vertices).ToArray(),
            Indices = [],
            Parts = parts,
            IsOriginalMri = false,
            SourceVolume = volume,
            SliceCrosshair = BuildSliceCrosshair(volume, axialIndex, coronalIndex, sagittalIndex),
            Center = center,
            Radius = MathF.Sqrt(maxDistanceSquared)
        };
    }

    public static VolumeMesh Rebuild(DicomVolume volume, RebuildSettings settings, int axialIndex, int coronalIndex, int sagittalIndex, IProgress<(int Percent, string Message)>? progress = null)
    {
        progress?.Report((5, "正在分析体素强度..."));
        GetIntensityRange(volume, out float min, out float max);

        float threshold = min + ((max - min) * settings.ThresholdRatio);
        progress?.Report((15, "正在生成 3D 等值面..."));

        int step = GetSamplingStep(volume, settings.SmoothingPasses);
        (Vector3[] vertices, int[] indices) = BuildIsoSurface(
            volume,
            (x, y, z) => volume.Voxels[z, y, x],
            threshold,
            step,
            progress,
            "正在生成 3D 等值面...");

        progress?.Report((88, "正在平滑 3D 表面..."));
        var vertexList = vertices.ToList();
        SmoothVertices(vertexList, settings.SmoothingPasses);
        vertices = vertexList.ToArray();

        if (vertices.Length == 0 || indices.Length < 3)
        {
            throw new InvalidOperationException("当前阈值未生成可用的 3D 等值面，请调整阈值后重试。");
        }

        Vector3 center = Vector3.Zero;
        foreach (Vector3 vertex in vertices)
        {
            center += vertex;
        }
        center /= vertices.Length;

        float maxDistanceSquared = 1f;
        foreach (Vector3 vertex in vertices)
        {
            maxDistanceSquared = Math.Max(maxDistanceSquared, Vector3.DistanceSquared(center, vertex));
        }

        progress?.Report((100, "3D 重建已就绪。"));
        return new VolumeMesh
        {
            Vertices = vertices,
            Indices = indices,
            IsOriginalMri = true,
            SourceVolume = volume,
            SliceCrosshair = BuildSliceCrosshair(volume, axialIndex, coronalIndex, sagittalIndex),
            Center = center,
            Radius = MathF.Sqrt(maxDistanceSquared)
        };
    }

    private static void GetIntensityRange(DicomVolume volume, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;

        foreach (float value in volume.Voxels)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        if (min == float.MaxValue)
        {
            min = 0f;
            max = 1f;
        }
    }

    private static int GetSamplingStep(DicomVolume volume, int smoothingPasses)
    {
        int maxDimension = Math.Max(volume.Depth, Math.Max(volume.Height, volume.Width));
        int step = maxDimension switch
        {
            > 320 => 4,
            > 180 => 3,
            > 96 => 2,
            _ => 1
        };

        return Math.Max(1, step - Math.Min(1, smoothingPasses));
    }

    private static VolumeMeshPart RebuildMaskPart(DicomVolume volume, NiftiOverlayLayer overlay)
    {
        int step = GetSamplingStep(volume, 1);
        (Vector3[] vertices, int[] indices) = BuildIsoSurface(
            volume,
            (x, y, z) => Math.Abs(overlay.Voxels[z, y, x]) > 1e-6f ? 1f : 0f,
            0.5f,
            step,
            null,
            string.Empty);

        return new VolumeMeshPart
        {
            Name = overlay.AnatomyStructure,
            Vertices = vertices,
            Indices = indices,
            Color = overlay.Color,
            SourceOverlay = overlay
        };
    }

    private static (Vector3[] Vertices, int[] Indices) BuildIsoSurface(
        DicomVolume volume,
        Func<int, int, int, float> valueAt,
        float isoValue,
        int step,
        IProgress<(int Percent, string Message)>? progress,
        string progressMessage)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        int maxZ = Math.Max(0, volume.Depth - 1);
        int maxY = Math.Max(0, volume.Height - 1);
        int maxX = Math.Max(0, volume.Width - 1);
        int sampledDepth = Math.Max(1, (maxZ + step - 1) / step);

        // Use heap-allocated reusable arrays to avoid large cumulative stack usage
        Vector3[] positionsArray = new Vector3[8];
        float[] valuesArray = new float[8];

        for (int z = 0, sampleIndex = 0; z < maxZ; z += step, sampleIndex++)
        {
            int z1 = Math.Min(maxZ, z + step);
            for (int y = 0; y < maxY; y += step)
            {
                int y1 = Math.Min(maxY, y + step);
                for (int x = 0; x < maxX; x += step)
                {
                    int x1 = Math.Min(maxX, x + step);

                    positionsArray[0] = new Vector3(x, y, z);
                    positionsArray[1] = new Vector3(x1, y, z);
                    positionsArray[2] = new Vector3(x1, y1, z);
                    positionsArray[3] = new Vector3(x, y1, z);
                    positionsArray[4] = new Vector3(x, y, z1);
                    positionsArray[5] = new Vector3(x1, y, z1);
                    positionsArray[6] = new Vector3(x1, y1, z1);
                    positionsArray[7] = new Vector3(x, y1, z1);

                    valuesArray[0] = valueAt(x, y, z);
                    valuesArray[1] = valueAt(x1, y, z);
                    valuesArray[2] = valueAt(x1, y1, z);
                    valuesArray[3] = valueAt(x, y1, z);
                    valuesArray[4] = valueAt(x, y, z1);
                    valuesArray[5] = valueAt(x1, y, z1);
                    valuesArray[6] = valueAt(x1, y1, z1);
                    valuesArray[7] = valueAt(x, y1, z1);

                    PolygonizeCube(volume, positionsArray, valuesArray, isoValue, vertices, indices);
                }
            }

            if (progress is not null && (sampleIndex % 4 == 0 || z + step >= maxZ))
            {
                int percent = 15 + (int)Math.Round(((sampleIndex + 1) / (double)sampledDepth) * 70d);
                progress.Report((Math.Min(percent, 85), progressMessage));
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    private static void PolygonizeCube(DicomVolume volume, ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> values, float isoValue, List<Vector3> vertices, List<int> indices)
    {
        ReadOnlySpan<int> tetrahedra =
        [
            0, 5, 1, 6,
            0, 1, 2, 6,
            0, 2, 3, 6,
            0, 3, 7, 6,
            0, 7, 4, 6,
            0, 4, 5, 6
        ];

        for (int i = 0; i < tetrahedra.Length; i += 4)
        {
            PolygonizeTetrahedron(
                volume,
                positions,
                values,
                tetrahedra[i],
                tetrahedra[i + 1],
                tetrahedra[i + 2],
                tetrahedra[i + 3],
                isoValue,
                vertices,
                indices);
        }
    }

    private static void PolygonizeTetrahedron(DicomVolume volume, ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> values, int a, int b, int c, int d, float isoValue, List<Vector3> vertices, List<int> indices)
    {
        int[] ids = new int[4] { a, b, c, d };
        int[] inside = new int[4];
        int[] outside = new int[4];
        int insideCount = 0;
        int outsideCount = 0;

        for (int i = 0; i < 4; i++)
        {
            int id = ids[i];
            if (values[id] >= isoValue)
            {
                inside[insideCount++] = id;
            }
            else
            {
                outside[outsideCount++] = id;
            }
        }

        if (insideCount == 0 || insideCount == 4)
        {
            return;
        }

        if (insideCount == 1)
        {
            AddTriangle(
                Interpolate(volume, positions[inside[0]], positions[outside[0]], values[inside[0]], values[outside[0]], isoValue),
                Interpolate(volume, positions[inside[0]], positions[outside[1]], values[inside[0]], values[outside[1]], isoValue),
                Interpolate(volume, positions[inside[0]], positions[outside[2]], values[inside[0]], values[outside[2]], isoValue),
                vertices,
                indices);
            return;
        }

        if (insideCount == 3)
        {
            AddTriangle(
                Interpolate(volume, positions[outside[0]], positions[inside[2]], values[outside[0]], values[inside[2]], isoValue),
                Interpolate(volume, positions[outside[0]], positions[inside[1]], values[outside[0]], values[inside[1]], isoValue),
                Interpolate(volume, positions[outside[0]], positions[inside[0]], values[outside[0]], values[inside[0]], isoValue),
                vertices,
                indices);
            return;
        }

        Vector3 p0 = Interpolate(volume, positions[inside[0]], positions[outside[0]], values[inside[0]], values[outside[0]], isoValue);
        Vector3 p1 = Interpolate(volume, positions[inside[0]], positions[outside[1]], values[inside[0]], values[outside[1]], isoValue);
        Vector3 p2 = Interpolate(volume, positions[inside[1]], positions[outside[0]], values[inside[1]], values[outside[0]], isoValue);
        Vector3 p3 = Interpolate(volume, positions[inside[1]], positions[outside[1]], values[inside[1]], values[outside[1]], isoValue);
        AddTriangle(p0, p1, p2, vertices, indices);
        AddTriangle(p1, p3, p2, vertices, indices);
    }

    private static Vector3 Interpolate(DicomVolume volume, Vector3 a, Vector3 b, float valueA, float valueB, float isoValue)
    {
        float delta = valueB - valueA;
        float t = Math.Abs(delta) < 1e-6f ? 0.5f : Math.Clamp((isoValue - valueA) / delta, 0f, 1f);
        Vector3 point = Vector3.Lerp(a, b, t);
        return NormalizePoint(volume, point.X, point.Y, point.Z);
    }

    private static void AddTriangle(Vector3 a, Vector3 b, Vector3 c, List<Vector3> vertices, List<int> indices)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
    }

    private static Vector3 NormalizePoint(DicomVolume volume, int x, int y, int z)
    {
        float nx = ((x / Math.Max(1f, volume.Width - 1f)) - 0.5f) * 2f;
        float ny = ((y / Math.Max(1f, volume.Height - 1f)) - 0.5f) * -2f;
        float nz = ((z / Math.Max(1f, volume.Depth - 1f)) - 0.5f) * 2f;
        return new Vector3(nx, ny, nz);
    }

    private static Vector3 NormalizePoint(DicomVolume volume, float x, float y, float z)
    {
        float nx = ((x / Math.Max(1f, volume.Width - 1f)) - 0.5f) * 2f;
        float ny = ((y / Math.Max(1f, volume.Height - 1f)) - 0.5f) * -2f;
        float nz = ((z / Math.Max(1f, volume.Depth - 1f)) - 0.5f) * 2f;
        return new Vector3(nx, ny, nz);
    }

    private static void SmoothVertices(List<Vector3> vertices, int passes)
    {
        if (passes <= 0 || vertices.Count < 3)
        {
            return;
        }

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 1; i < vertices.Count - 1; i++)
            {
                vertices[i] = Vector3.Lerp(vertices[i], (vertices[i - 1] + vertices[i] + vertices[i + 1]) / 3f, 0.35f);
            }
        }
    }

    private static Vector3[] BuildSliceCrosshair(DicomVolume volume, int axialIndex, int coronalIndex, int sagittalIndex)
    {
        float x = ((Math.Clamp(sagittalIndex, 0, Math.Max(0, volume.Width - 1)) / Math.Max(1f, volume.Width - 1f)) - 0.5f) * 2f;
        float y = ((Math.Clamp(coronalIndex, 0, Math.Max(0, volume.Height - 1)) / Math.Max(1f, volume.Height - 1f)) - 0.5f) * -2f;
        float z = ((Math.Clamp(axialIndex, 0, Math.Max(0, volume.Depth - 1)) / Math.Max(1f, volume.Depth - 1f)) - 0.5f) * 2f;

        return
        [
            new Vector3(-1f, y, z), new Vector3(1f, y, z),
            new Vector3(x, -1f, z), new Vector3(x, 1f, z),
            new Vector3(x, y, -1f), new Vector3(x, y, 1f)
        ];
    }
}
