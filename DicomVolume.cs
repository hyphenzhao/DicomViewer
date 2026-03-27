using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Render;
using System.IO.Compression;

namespace DicomViewer;

internal sealed class DicomVolume
{
    public required string SourcePath { get; init; }
    public required IReadOnlyList<string> SourceFiles { get; init; }
    public required float[,,] Voxels { get; init; }
    public required OrientationInfo Orientation { get; init; }

    public int Depth => Voxels.GetLength(0);
    public int Height => Voxels.GetLength(1);
    public int Width => Voxels.GetLength(2);

    public int AxialIndex => Depth > 0 ? Depth / 2 : 0;
    public int CoronalIndex => Height > 0 ? Height / 2 : 0;
    public int SagittalIndex => Width > 0 ? Width / 2 : 0;

    public static DicomVolume LoadFromFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("Could not locate the selected folder.");
        }

        var seriesFiles = DiscoverSeriesFiles(folder);
        if (seriesFiles.Count == 0)
        {
            throw new InvalidOperationException("No DICOM files were found in the selected folder.");
        }

        var slices = new List<SliceData>();
        foreach (string path in seriesFiles)
        {
            try
            {
                var file = DicomFile.Open(path);
                var dataset = file.Dataset;
                var pixelData = DicomPixelData.Create(dataset);

                if (pixelData.NumberOfFrames < 1)
                {
                    continue;
                }

                int width = pixelData.Width;
                int height = pixelData.Height;
                var frame = PixelDataFactory.Create(pixelData, 0);
                var pixels = new float[height, width];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixels[y, x] = GetPixelValue(frame, x, y);
                    }
                }

                ApplyRescale(dataset, pixels);
                ApplyMonochrome1(dataset, pixels);

                slices.Add(new SliceData
                {
                    Path = path,
                    Pixels = pixels,
                    Width = width,
                    Height = height,
                    SeriesInstanceUid = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty),
                    InstanceNumber = dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, int.MaxValue),
                    SliceLocation = dataset.TryGetSingleValue(DicomTag.SliceLocation, out double sliceLocation) ? sliceLocation : (double?)null,
                    ImagePositionZ = TryGetImagePositionZ(dataset),
                    Position = TryGetImagePosition(dataset),
                    Orientation = TryGetImageOrientation(dataset)
                });
            }
            catch
            {
                // Ignore non-DICOM or incompatible files in the folder.
            }
        }

        if (slices.Count == 0)
        {
            throw new InvalidOperationException("No readable DICOM image slices were found in the selected folder.");
        }

        string targetSeriesUid = ChooseSeriesUid(slices);
        var orderedSlices = slices
            .Where(s => string.Equals(s.SeriesInstanceUid, targetSeriesUid, StringComparison.Ordinal))
            .ToList();

        if (orderedSlices.Count == 0)
        {
            orderedSlices = slices;
        }

        int referenceWidth = orderedSlices[0].Width;
        int referenceHeight = orderedSlices[0].Height;
        orderedSlices = orderedSlices
            .Where(s => s.Width == referenceWidth && s.Height == referenceHeight)
            .ToList();

        if (orderedSlices.Count == 0)
        {
            throw new InvalidOperationException("No consistently sized DICOM slices were available to form a volume.");
        }

        orderedSlices.Sort(CompareSlices);
        OrientationInfo orientation = BuildOrientationInfo(orderedSlices);

        var voxels = new float[orderedSlices.Count, referenceHeight, referenceWidth];
        for (int z = 0; z < orderedSlices.Count; z++)
        {
            for (int y = 0; y < referenceHeight; y++)
            {
                for (int x = 0; x < referenceWidth; x++)
                {
                    voxels[z, y, x] = orderedSlices[z].Pixels[y, x];
                }
            }
        }

        return new DicomVolume
        {
            SourcePath = folder,
            SourceFiles = orderedSlices.Select(s => s.Path).ToList(),
            Voxels = voxels,
            Orientation = orientation
        };
    }

    public static DicomVolume LoadFromNifti(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Could not locate the selected NIfTI file.", path);
        }

        using Stream stream = OpenNiftiStream(path);
        using var reader = new BinaryReader(stream);

        int sizeofHdr = reader.ReadInt32();
        if (sizeofHdr != 348)
        {
            throw new InvalidOperationException("The selected file is not a supported NIfTI-1 volume.");
        }

        stream.Position = 40;
        short[] dim = ReadInt16Array(reader, 8);
        short rank = dim[0];
        if (rank < 3)
        {
            throw new InvalidOperationException("The selected NIfTI file does not contain a 3D volume.");
        }

        int width = dim[1];
        int height = dim[2];
        int depth = dim[3];
        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new InvalidOperationException("The selected NIfTI file has invalid dimensions.");
        }

        stream.Position = 70;
        short datatype = reader.ReadInt16();
        short bitpix = reader.ReadInt16();

        stream.Position = 108;
        float voxOffset = reader.ReadSingle();

        stream.Position = 344;
        string magic = new string(reader.ReadChars(4)).TrimEnd('\0');
        if (magic is not ("n+1" or "ni1"))
        {
            throw new InvalidOperationException("The selected file is not a supported NIfTI-1 volume.");
        }

        stream.Position = (long)Math.Max(voxOffset, 352f);
        var voxels = new float[depth, height, width];

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    voxels[z, y, x] = ReadNiftiValue(reader, datatype, bitpix);
                }
            }
        }

        return new DicomVolume
        {
            SourcePath = path,
            SourceFiles = new[] { path },
            Voxels = voxels,
            Orientation = new OrientationInfo
            {
                FlipAxialVertical = true,
                FlipCoronalVertical = true,
                FlipSagittalVertical = true,
                FlipSagittalHorizontal = false
            }
        };
    }

    private static List<string> DiscoverSeriesFiles(string folder)
    {
        return Directory
            .EnumerateFiles(folder)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Stream OpenNiftiStream(string path)
    {
        FileStream fileStream = File.OpenRead(path);
        string extension = Path.GetExtension(path);

        if (string.Equals(extension, ".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        return fileStream;
    }

    private static short[] ReadInt16Array(BinaryReader reader, int count)
    {
        var values = new short[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = reader.ReadInt16();
        }

        return values;
    }

    private static float ReadNiftiValue(BinaryReader reader, short datatype, short bitpix)
    {
        return datatype switch
        {
            2 when bitpix == 8 => reader.ReadByte(),
            4 when bitpix == 16 => reader.ReadInt16(),
            8 when bitpix == 32 => reader.ReadInt32(),
            16 when bitpix == 32 => reader.ReadSingle(),
            64 when bitpix == 64 => (float)reader.ReadDouble(),
            256 when bitpix == 8 => unchecked((sbyte)reader.ReadByte()),
            512 when bitpix == 16 => reader.ReadUInt16(),
            768 when bitpix == 32 => reader.ReadUInt32(),
            _ => throw new NotSupportedException($"Unsupported NIfTI datatype: {datatype} ({bitpix} bits).")
        };
    }

    private static string ChooseSeriesUid(List<SliceData> slices)
    {
        return slices
            .GroupBy(s => s.SeriesInstanceUid)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;
    }

    private static int CompareSlices(SliceData a, SliceData b)
    {
        if (a.Position is not null && b.Position is not null)
        {
            double byProjection = a.Position.Value.Dot(a.SliceNormal) - b.Position.Value.Dot(a.SliceNormal);
            if (Math.Abs(byProjection) > 1e-6)
            {
                return byProjection < 0 ? -1 : 1;
            }
        }

        int byZ = Nullable.Compare(a.ImagePositionZ, b.ImagePositionZ);
        if (byZ != 0) return byZ;

        int byLocation = Nullable.Compare(a.SliceLocation, b.SliceLocation);
        if (byLocation != 0) return byLocation;

        int byInstance = a.InstanceNumber.CompareTo(b.InstanceNumber);
        if (byInstance != 0) return byInstance;

        return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static OrientationInfo BuildOrientationInfo(List<SliceData> slices)
    {
        SliceData first = slices[0];
        var rowAxis = first.RowDirection;
        var columnAxis = first.ColumnDirection;
        var sliceAxis = first.SliceNormal;

        bool flipAxialVertical = columnAxis.Z > 0;
        bool flipCoronalVertical = sliceAxis.Z > 0;
        bool flipSagittalVertical = sliceAxis.Z > 0;
        bool flipSagittalHorizontal = columnAxis.X < 0;

        return new OrientationInfo
        {
            FlipAxialVertical = flipAxialVertical,
            FlipCoronalVertical = flipCoronalVertical,
            FlipSagittalVertical = flipSagittalVertical,
            FlipSagittalHorizontal = flipSagittalHorizontal
        };
    }

    private static double? TryGetImagePositionZ(DicomDataset dataset)
    {
        if (!dataset.TryGetValues(DicomTag.ImagePositionPatient, out double[]? values) || values is null || values.Length < 3)
        {
            return null;
        }

        return values[2];
    }

    private static Vector3D? TryGetImagePosition(DicomDataset dataset)
    {
        if (!dataset.TryGetValues(DicomTag.ImagePositionPatient, out double[]? values) || values is null || values.Length < 3)
        {
            return null;
        }

        return new Vector3D(values[0], values[1], values[2]);
    }

    private static (Vector3D Row, Vector3D Column)? TryGetImageOrientation(DicomDataset dataset)
    {
        if (!dataset.TryGetValues(DicomTag.ImageOrientationPatient, out double[]? values) || values is null || values.Length < 6)
        {
            return null;
        }

        Vector3D row = new(values[0], values[1], values[2]);
        Vector3D column = new(values[3], values[4], values[5]);

        if (row.TryNormalize(out Vector3D normalizedRow) && column.TryNormalize(out Vector3D normalizedColumn))
        {
            return (normalizedRow, normalizedColumn);
        }

        return null;
    }

    private static float GetPixelValue(IPixelData pixelData, int x, int y)
    {
        return pixelData switch
        {
            GrayscalePixelDataU8 p => p.Data[y * p.Width + x],
            GrayscalePixelDataU16 p => p.Data[y * p.Width + x],
            GrayscalePixelDataS16 p => p.Data[y * p.Width + x],
            GrayscalePixelDataU32 p => p.Data[y * p.Width + x],
            GrayscalePixelDataS32 p => p.Data[y * p.Width + x],
            ColorPixelData24 p => AverageColor24(p, x, y),
            _ => (float)pixelData.GetPixel(x, y)
        };
    }

    private static float AverageColor24(ColorPixelData24 p, int x, int y)
    {
        int index = (y * p.Width + x) * 3;
        return (p.Data[index] + p.Data[index + 1] + p.Data[index + 2]) / 3f;
    }

    private static void ApplyRescale(DicomDataset dataset, float[,] pixels)
    {
        double slope = dataset.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
        double intercept = dataset.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);

        if (Math.Abs(slope - 1.0) < 1e-12 && Math.Abs(intercept) < 1e-12)
        {
            return;
        }

        int h = pixels.GetLength(0);
        int w = pixels.GetLength(1);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y, x] = (float)(pixels[y, x] * slope + intercept);
    }

    private static void ApplyMonochrome1(DicomDataset dataset, float[,] pixels)
    {
        string photometric = dataset.GetSingleValueOrDefault(DicomTag.PhotometricInterpretation, string.Empty).Trim().ToUpperInvariant();
        if (photometric != "MONOCHROME1")
        {
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (float value in pixels)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        int h = pixels.GetLength(0);
        int w = pixels.GetLength(1);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y, x] = max - (pixels[y, x] - min);
    }

    private sealed class SliceData
    {
        public required string Path { get; init; }
        public required float[,] Pixels { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required string SeriesInstanceUid { get; init; }
        public required int InstanceNumber { get; init; }
        public required double? SliceLocation { get; init; }
        public required double? ImagePositionZ { get; init; }
        public required Vector3D? Position { get; init; }
        public required (Vector3D Row, Vector3D Column)? Orientation { get; init; }

        public Vector3D RowDirection => Orientation?.Row ?? Vector3D.UnitX;
        public Vector3D ColumnDirection => Orientation?.Column ?? Vector3D.UnitY;
        public Vector3D SliceNormal => RowDirection.Cross(ColumnDirection).NormalizeOrDefault(Vector3D.UnitZ);
    }

    internal sealed class OrientationInfo
    {
        public required bool FlipAxialVertical { get; init; }
        public required bool FlipCoronalVertical { get; init; }
        public required bool FlipSagittalVertical { get; init; }
        public required bool FlipSagittalHorizontal { get; init; }
    }

    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public static Vector3D UnitX => new(1, 0, 0);
        public static Vector3D UnitY => new(0, 1, 0);
        public static Vector3D UnitZ => new(0, 0, 1);

        public double Dot(Vector3D other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        public Vector3D Cross(Vector3D other) => new(
            (Y * other.Z) - (Z * other.Y),
            (Z * other.X) - (X * other.Z),
            (X * other.Y) - (Y * other.X));

        public bool TryNormalize(out Vector3D normalized)
        {
            double length = Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
            if (length < 1e-12)
            {
                normalized = default;
                return false;
            }

            normalized = new Vector3D(X / length, Y / length, Z / length);
            return true;
        }

        public Vector3D NormalizeOrDefault(Vector3D fallback)
        {
            return TryNormalize(out Vector3D normalized) ? normalized : fallback;
        }
    }
}