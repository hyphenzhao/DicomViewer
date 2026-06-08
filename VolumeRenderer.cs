using System.Drawing;
using System.Drawing.Imaging;

namespace DicomViewer;

internal static class VolumeRenderer
{
    private const float OverlayAlpha = 0.45f;

    public static Bitmap RenderAxial(DicomVolume volume, int sliceIndex)
    {
        sliceIndex = Math.Clamp(sliceIndex, 0, volume.Depth - 1);
        int height = volume.Height;
        int width = volume.Width;
        var buffer = new float[height, width];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            buffer[y, x] = volume.ShowOriginalIn2D ? volume.Voxels[sliceIndex, y, x] : 0f;

        if (volume.Orientation.FlipAxialVertical)
        {
            buffer = FlipVertical(buffer);
        }

        return ToBitmap(buffer, volume.Overlays, (layer, y, x) => layer.Voxels[sliceIndex, y, x]);
    }

    public static Bitmap RenderCoronal(DicomVolume volume, int rowIndex)
    {
        rowIndex = Math.Clamp(rowIndex, 0, volume.Height - 1);
        int depth = volume.Depth;
        int width = volume.Width;
        var buffer = new float[depth, width];

        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
            buffer[z, x] = volume.ShowOriginalIn2D ? volume.Voxels[z, rowIndex, x] : 0f;

        if (volume.Orientation.FlipCoronalVertical)
        {
            buffer = FlipVertical(buffer);
        }

        return ToBitmap(buffer, volume.Overlays, (layer, y, x) => layer.Voxels[y, rowIndex, x]);
    }

    public static Bitmap RenderSagittal(DicomVolume volume, int columnIndex)
    {
        columnIndex = Math.Clamp(columnIndex, 0, volume.Width - 1);
        int depth = volume.Depth;
        int height = volume.Height;
        var buffer = new float[depth, height];

        for (int z = 0; z < depth; z++)
        for (int y = 0; y < height; y++)
            buffer[z, y] = volume.ShowOriginalIn2D ? volume.Voxels[z, y, columnIndex] : 0f;

        if (volume.Orientation.FlipSagittalVertical)
        {
            buffer = FlipVertical(buffer);
        }

        if (volume.Orientation.FlipSagittalHorizontal)
        {
            buffer = FlipHorizontal(buffer);
        }

        return ToBitmap(buffer, volume.Overlays, (layer, y, x) => layer.Voxels[y, x, columnIndex]);
    }

    private static float[,] FlipVertical(float[,] values)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        var flipped = new float[height, width];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            flipped[y, x] = values[height - 1 - y, x];

        return flipped;
    }

    private static float[,] FlipHorizontal(float[,] values)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        var flipped = new float[height, width];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            flipped[y, x] = values[y, width - 1 - x];

        return flipped;
    }

    private static Bitmap ToBitmap(float[,] values, IReadOnlyList<NiftiOverlayLayer> overlays, Func<NiftiOverlayLayer, int, int, float> overlayValueSelector)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (float value in values)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        float range = Math.Max(max - min, 1e-6f);
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);

        unsafe
        {
            byte* ptr = (byte*)data.Scan0;
            for (int y = 0; y < height; y++)
            {
                byte* row = ptr + (y * data.Stride);
                for (int x = 0; x < width; x++)
                {
                    byte gray = (byte)Math.Clamp((int)(((values[y, x] - min) / range) * 255f), 0, 255);
                    int r = gray;
                    int g = gray;
                    int b = gray;

                    foreach (NiftiOverlayLayer overlay in overlays)
                    {
                        if (!overlay.Visible || Math.Abs(overlayValueSelector(overlay, y, x)) <= 1e-6f)
                        {
                            continue;
                        }

                        r = Blend(r, overlay.Color.R);
                        g = Blend(g, overlay.Color.G);
                        b = Blend(b, overlay.Color.B);
                    }

                    int offset = x * 3;
                    row[offset] = (byte)b;
                    row[offset + 1] = (byte)g;
                    row[offset + 2] = (byte)r;
                }
            }
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }

    private static int Blend(int baseValue, int overlayValue)
    {
        return Math.Clamp((int)Math.Round((baseValue * (1f - OverlayAlpha)) + (overlayValue * OverlayAlpha)), 0, 255);
    }
}