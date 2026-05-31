using System.Numerics;

namespace DicomViewer;

internal static class VolumeRebuilder
{
    public static VolumeMesh Rebuild(DicomVolume volume, RebuildSettings settings, int axialIndex, int coronalIndex, int sagittalIndex, IProgress<(int Percent, string Message)>? progress = null)
    {
        progress?.Report((5, "正在分析体素强度..."));
        GetIntensityRange(volume, out float min, out float max);

        float threshold = min + ((max - min) * settings.ThresholdRatio);
        progress?.Report((15, "正在生成 3D 表面点..."));

        int step = GetSamplingStep(volume, settings.SmoothingPasses);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        Vector3 sum = Vector3.Zero;
        float maxDistanceSquared = 1f;

        int sampledDepth = Math.Max(1, (volume.Depth + step - 1) / step);
        for (int z = 0, sampleIndex = 0; z < volume.Depth; z += step, sampleIndex++)
        {
            for (int y = 0; y < volume.Height; y += step)
            {
                for (int x = 0; x < volume.Width; x += step)
                {
                    float value = volume.Voxels[z, y, x];
                    if (value < threshold || !IsSurfaceVoxel(volume, x, y, z, threshold, step))
                    {
                        continue;
                    }

                    Vector3 point = NormalizePoint(volume, x, y, z);
                    vertices.Add(point);
                    sum += point;
                }
            }

            int percent = 15 + (int)Math.Round(((sampleIndex + 1) / (double)sampledDepth) * 70d);
            progress?.Report((Math.Min(percent, 85), "正在生成 3D 表面点..."));
        }

        progress?.Report((88, "正在平滑 3D 表面..."));
        SmoothVertices(vertices, settings.SmoothingPasses);

        progress?.Report((93, "正在对点云进行三角化..."));
        for (int i = 0; i + 2 < vertices.Count; i += 3)
        {
            indices.Add(i);
            indices.Add(i + 1);
            indices.Add(i + 2);
        }

        if (vertices.Count == 0)
        {
            vertices.AddRange([
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.0f, 0.5f, 0.0f)
            ]);
            indices.AddRange([0, 1, 2]);
            sum = vertices[0] + vertices[1] + vertices[2];
        }

        Vector3 center = sum / vertices.Count;
        foreach (Vector3 vertex in vertices)
        {
            maxDistanceSquared = Math.Max(maxDistanceSquared, Vector3.DistanceSquared(center, vertex));
        }

        progress?.Report((100, "3D 重建已就绪。"));
        return new VolumeMesh
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
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

    private static bool IsSurfaceVoxel(DicomVolume volume, int x, int y, int z, float threshold, int step)
    {
        if (x == 0 || y == 0 || z == 0 || x >= volume.Width - step || y >= volume.Height - step || z >= volume.Depth - step)
        {
            return true;
        }

        return volume.Voxels[z, y, Math.Max(0, x - step)] < threshold
            || volume.Voxels[z, y, Math.Min(volume.Width - 1, x + step)] < threshold
            || volume.Voxels[z, Math.Max(0, y - step), x] < threshold
            || volume.Voxels[z, Math.Min(volume.Height - 1, y + step), x] < threshold
            || volume.Voxels[Math.Max(0, z - step), y, x] < threshold
            || volume.Voxels[Math.Min(volume.Depth - 1, z + step), y, x] < threshold;
    }

    private static Vector3 NormalizePoint(DicomVolume volume, int x, int y, int z)
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
