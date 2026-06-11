using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DicomViewer;

internal sealed class CartiMorphPipeline
{
    private readonly PythonSettings _settings;
    private readonly ExternalProcessRunner _runner = new();

    public CartiMorphPipeline(PythonSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Runs the full segmentation pipeline on a loaded volume.
    /// </summary>
    public async Task<PipelineStepResult> RunSegmentationAsync(
        DicomVolume volume,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
        {
            return PipelineStepResult.Fail("Python 环境未配置。请在 工具 > 设置 中配置 Python 解释器路径。");
        }

        string? tempDir = null;
        try
        {
            // Ensure temp directory exists
            tempDir = CreateTempDir();

            // Step 1: Save volume as NIfTI
            progress?.Report("正在将体数据保存为 NIfTI...");
            string inputNifti = Path.Combine(tempDir, "input.nii.gz");
            SaveAsNiftiGz(volume, inputNifti);

            // Step 2: Run segmentation script
            string scriptPath = Path.Combine(_settings.ScriptsDirectory, "cmt_segmentation.py");
            if (!File.Exists(scriptPath))
            {
                return PipelineStepResult.Fail($"找不到脚本文件: {scriptPath}");
            }

            string outputDir = Path.Combine(tempDir, "segmentation_output");
            string args = $"\"{scriptPath}\" --input \"{inputNifti}\" --model-dir \"{_settings.ModelsDirectory}\" --output-dir \"{outputDir}\"";

            progress?.Report("正在启动分割推理...");
            var result = await ExternalProcessRunner.RunAsync(
                _settings.PythonInterpreterPath,
                args,
                progress,
                ct);

            if (!result.Success)
            {
                return PipelineStepResult.Fail($"分割脚本退出码: {result.ExitCode}\n\n{result.ErrorText}");
            }

            // Step 3: Load results as overlays
            string maskPath = Path.Combine(outputDir, "segmentation_mask.nii.gz");
            if (!File.Exists(maskPath))
            {
                return PipelineStepResult.Fail($"分割结果文件未生成: {maskPath}");
            }

            // Load the segmentation overlay
            var overlay = LoadNiftiAsOverlay(maskPath, "分割掩膜", Color.Lime, OverlayKind.LabelMap);
            overlay.AnatomyStructure = "分割结果";
            overlay.CanBuild3D = true;

            // Load label info if available
            string infoPath = Path.Combine(outputDir, "segmentation_info.json");
            string? infoJson = File.Exists(infoPath) ? await File.ReadAllTextAsync(infoPath, ct) : null;

            return PipelineStepResult.Ok(
                $"分割完成。输出目录: {outputDir}",
                new[] { overlay },
                infoJson);
        }
        catch (OperationCanceledException)
        {
            return PipelineStepResult.Fail("操作已取消。");
        }
        catch (Exception ex)
        {
            return PipelineStepResult.Fail($"分割失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs quantification on an existing segmentation mask.
    /// </summary>
    public async Task<PipelineStepResult> RunQuantificationAsync(
        string segmentationNiftiPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
        {
            return PipelineStepResult.Fail("Python 环境未配置。请在 工具 > 设置 中配置 Python 解释器路径。");
        }

        string? tempDir = null;
        try
        {
            tempDir = CreateTempDir();
            string outputDir = Path.Combine(tempDir, "quantification_output");

            string scriptPath = Path.Combine(_settings.ScriptsDirectory, "cmt_quantification.py");
            if (!File.Exists(scriptPath))
            {
                return PipelineStepResult.Fail($"找不到脚本文件: {scriptPath}");
            }

            string args = $"\"{scriptPath}\" --seg \"{segmentationNiftiPath}\" --output-dir \"{outputDir}\"";

            progress?.Report("正在启动量化分析...");
            var result = await ExternalProcessRunner.RunAsync(
                _settings.PythonInterpreterPath,
                args,
                progress,
                ct);

            if (!result.Success)
            {
                return PipelineStepResult.Fail($"量化分析脚本退出码: {result.ExitCode}\n\n{result.ErrorText}");
            }

            // Load thickness map as overlay
            string thicknessPath = Path.Combine(outputDir, "thickness_map.nii.gz");
            var overlays = new List<NiftiOverlayLayer>();

            if (File.Exists(thicknessPath))
            {
                overlays.Add(LoadNiftiAsOverlay(thicknessPath, "软骨厚度图", Color.Magenta, OverlayKind.Thickness));
            }

            // Load quantification report
            string reportPath = Path.Combine(outputDir, "quantification_report.json");
            string? reportJson = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath, ct) : null;

            return PipelineStepResult.Ok(
                $"量化分析完成。输出目录: {outputDir}",
                overlays,
                reportJson);
        }
        catch (OperationCanceledException)
        {
            return PipelineStepResult.Fail("操作已取消。");
        }
        catch (Exception ex)
        {
            return PipelineStepResult.Fail($"量化分析失败: {ex.Message}");
        }
    }

    private string CreateTempDir()
    {
        string dir = string.IsNullOrWhiteSpace(_settings.TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "DicomViewer_CMT")
            : _settings.TempDirectory;

        string sessionDir = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }

    /// <summary>
    /// Saves a DicomVolume as a gzip-compressed NIfTI-1 file.
    /// </summary>
    private static void SaveAsNiftiGz(DicomVolume volume, string path)
    {
        int depth = volume.Depth;
        int height = volume.Height;
        int width = volume.Width;

        // Build the entire NIfTI-1 file in memory first (GZipStream does not support seeking).
        const int headerSize = 348;
        const int voxOffset = 352;
        int voxelCount = depth * height * width;
        int fileSize = voxOffset + voxelCount * sizeof(float);
        byte[] buffer = new byte[fileSize];

        // --- NIfTI-1 header (348 bytes), written sequentially ---
        int pos = 0;

        // Bytes 0-3: sizeof_hdr
        BitConverter.GetBytes(headerSize).CopyTo(buffer, pos); pos += 4;

        // Bytes 4-39: padding (36 bytes of zero)
        pos += 36;

        // Bytes 40-55: dim[8] (8 × short = 16 bytes)
        BitConverter.GetBytes((short)3).CopyTo(buffer, pos); pos += 2;     // dim[0] = rank
        BitConverter.GetBytes((short)width).CopyTo(buffer, pos); pos += 2; // dim[1]
        BitConverter.GetBytes((short)height).CopyTo(buffer, pos); pos += 2;// dim[2]
        BitConverter.GetBytes((short)depth).CopyTo(buffer, pos); pos += 2; // dim[3]
        for (int i = 0; i < 4; i++)
        {
            BitConverter.GetBytes((short)1).CopyTo(buffer, pos); pos += 2; // dim[4..7]
        }

        // Bytes 56-69: intent_p1, intent_p2, intent_p3 (float32 each), intent_code (short)
        pos += 14;

        // Bytes 70-71: datatype = 16 (float32)
        BitConverter.GetBytes((short)16).CopyTo(buffer, pos); pos += 2;

        // Bytes 72-73: bitpix = 32
        BitConverter.GetBytes((short)32).CopyTo(buffer, pos); pos += 2;

        // Byte 74-75: slice_start (short, padding)
        BitConverter.GetBytes((short)0).CopyTo(buffer, pos); pos += 2;

        // Bytes 76-107: pixdim[0..7] (8 × float32).  Must be non-zero for dims 1-3 or nibabel
        // will error.  Since we don't have spacing info, write 1.0 as "unknown unit spacing".
        BitConverter.GetBytes(1f).CopyTo(buffer, pos); pos += 4;  // pixdim[0]
        BitConverter.GetBytes(1f).CopyTo(buffer, pos); pos += 4;  // pixdim[1]
        BitConverter.GetBytes(1f).CopyTo(buffer, pos); pos += 4;  // pixdim[2]
        BitConverter.GetBytes(1f).CopyTo(buffer, pos); pos += 4;  // pixdim[3]
        BitConverter.GetBytes(0f).CopyTo(buffer, pos); pos += 4;  // pixdim[4]
        BitConverter.GetBytes(0f).CopyTo(buffer, pos); pos += 4;  // pixdim[5]
        BitConverter.GetBytes(0f).CopyTo(buffer, pos); pos += 4;  // pixdim[6]
        BitConverter.GetBytes(0f).CopyTo(buffer, pos); pos += 4;  // pixdim[7]

        // Bytes 108-111: vox_offset = 352
        BitConverter.GetBytes((float)voxOffset).CopyTo(buffer, pos); pos += 4;

        // Bytes 112-115: scl_slope = 1
        BitConverter.GetBytes(1f).CopyTo(buffer, pos); pos += 4;

        // Bytes 116-119: scl_inter = 0
        BitConverter.GetBytes(0f).CopyTo(buffer, pos); pos += 4;

        // Bytes 120-122: slice_end, slice_code (padding)
        pos += 3;

        // Byte 123: xyzt_units = 2 (mm)
        buffer[pos++] = 2;

        // Bytes 124-343: padding to magic
        pos = 344;

        // Bytes 344-347: magic = "n+1\0"
        buffer[pos++] = (byte)'n';
        buffer[pos++] = (byte)'+';
        buffer[pos++] = (byte)'1';
        buffer[pos++] = 0;

        // pos should now be 348; pad to vox_offset (352)
        // (already at 348, so 4 more bytes of zero padding)
        pos = voxOffset;

        // --- Voxel data: float32, z-major ---
        for (int z = 0; z < depth; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            BitConverter.GetBytes(volume.Voxels[z, y, x]).CopyTo(buffer, pos);
            pos += sizeof(float);
        }

        // Write the whole buffer through GZipStream
        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
        gzipStream.Write(buffer, 0, buffer.Length);
    }

    /// <summary>
    /// Loads a NIfTI file as a NiftiOverlayLayer, keeping only non-zero voxels for efficiency.
    /// </summary>
    private static NiftiOverlayLayer LoadNiftiAsOverlay(string path, string displayName, Color defaultColor, OverlayKind kind)
    {
        // Read NIfTI header
        using Stream stream = OpenNiftiStream(path);
        using var reader = new BinaryReader(stream);

        int sizeofHdr = reader.ReadInt32();
        if (sizeofHdr != 348)
        {
            throw new InvalidOperationException($"不是有效的 NIfTI-1 文件: {path}");
        }

        stream.Position = 40;
        short[] dim = new short[8];
        for (int i = 0; i < 8; i++) dim[i] = reader.ReadInt16();

        int width = dim[1];
        int height = dim[2];
        int depth = dim[3];

        stream.Position = 70;
        short datatype = reader.ReadInt16();
        short bitpix = reader.ReadInt16();

        stream.Position = 108;
        float voxOffset = reader.ReadSingle();

        stream.Position = (long)Math.Max(voxOffset, 352f);

        var voxels = new float[depth, height, width];
        for (int z = 0; z < depth; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            voxels[z, y, x] = ReadNiftiValue(reader, datatype, bitpix);
        }

        return new NiftiOverlayLayer
        {
            Path = path,
            Name = displayName,
            Voxels = voxels,
            Color = defaultColor,
            Kind = kind,
            AnatomyStructure = displayName,
            CanBuild3D = kind == OverlayKind.LabelMap,
            Visible = true
        };
    }

    private static Stream OpenNiftiStream(string path)
    {
        string extension = Path.GetExtension(path);
        bool isGz = string.Equals(extension, ".gz", StringComparison.OrdinalIgnoreCase);

        if (isGz)
        {
            // GZipStream does not support seeking, so decompress the entire file into memory.
            using var fileStream = File.OpenRead(path);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        return File.OpenRead(path);
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
            _ => reader.ReadSingle()
        };
    }
}

internal sealed class PipelineStepResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<NiftiOverlayLayer> Overlays { get; init; } = [];
    public string? JsonReport { get; init; }
    public string? Error { get; init; }

    public static PipelineStepResult Ok(string message, IReadOnlyList<NiftiOverlayLayer>? overlays = null, string? jsonReport = null)
    {
        return new PipelineStepResult
        {
            Success = true,
            Message = message,
            Overlays = overlays ?? [],
            JsonReport = jsonReport
        };
    }

    public static PipelineStepResult Fail(string error)
    {
        return new PipelineStepResult
        {
            Success = false,
            Message = error,
            Error = error
        };
    }
}
