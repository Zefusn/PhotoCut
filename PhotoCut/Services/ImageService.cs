using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace PhotoCut.Services;

public class ImageService
{
    private static readonly Dictionary<string, SoftwareBitmapSource> _thumbnailCache = new();

    public async Task<SoftwareBitmapSource?> LoadThumbnailAsync(string filePath, bool isArw)
    {
        if (_thumbnailCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        try
        {
            SoftwareBitmap? bitmap;

            if (isArw)
            {
                bitmap = await LoadArwBitmapAsync(filePath, 200);
            }
            else
            {
                bitmap = await DecodeJpgBitmapAsync(filePath, 200);
            }

            if (bitmap == null) return null;

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);

            _thumbnailCache[filePath] = source;
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Thumbnail load error for {filePath}: {ex.Message}");
            return null;
        }
    }

    public async Task<SoftwareBitmapSource?> LoadFullImageAsync(string filePath, bool isArw)
    {
        try
        {
            SoftwareBitmap? bitmap;

            if (isArw)
            {
                bitmap = await LoadArwBitmapAsync(filePath, 4000, preferLargest: true);
            }
            else
            {
                bitmap = await DecodeJpgBitmapAsync(filePath, 4000);
            }

            if (bitmap == null) return null;

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Full image load error for {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 使用 StorageFile 解码图片。maxSize > 0 时缩放，maxSize <= 0 时加载原图。
    /// </summary>
    private async Task<SoftwareBitmap?> DecodeJpgBitmapAsync(string filePath, int maxSize)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var originalWidth = (int)decoder.PixelWidth;
            var originalHeight = (int)decoder.PixelHeight;

            if (originalWidth == 0 || originalHeight == 0) return null;

            bool useOriginalSize = maxSize <= 0;
            int targetWidth, targetHeight;

            if (useOriginalSize)
            {
                targetWidth = originalWidth;
                targetHeight = originalHeight;
            }
            else
            {
                if (originalWidth >= originalHeight)
                {
                    targetWidth = Math.Min(maxSize, originalWidth);
                    targetHeight = Math.Max(1, (int)(originalHeight * ((double)targetWidth / originalWidth)));
                }
                else
                {
                    targetHeight = Math.Min(maxSize, originalHeight);
                    targetWidth = Math.Max(1, (int)(originalWidth * ((double)targetHeight / originalHeight)));
                }
            }

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)targetWidth,
                ScaledHeight = (uint)targetHeight,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            var pixels = pixelData.DetachPixelData();

            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                targetWidth,
                targetHeight,
                BitmapAlphaMode.Premultiplied);

            softwareBitmap.CopyFromBuffer(pixels.AsBuffer());
            return softwareBitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Decode error for {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 提取 ARW 内嵌 JPEG 并解码
    /// </summary>
    /// <param name="filePath">ARW 文件路径</param>
    /// <param name="maxSize">缩略图最大尺寸，0 表示原图</param>
    /// <param name="preferLargest">是否优先选最大的（预览模式）</param>
    private async Task<SoftwareBitmap?> LoadArwBitmapAsync(string filePath, int maxSize, bool preferLargest = false)
    {
        var segments = await FindEmbeddedJpegsAsync(filePath);
        if (segments.Count == 0)
        {
            Debug.WriteLine($"No embedded JPEG found in: {filePath}");
            return CreatePlaceholderBitmap();
        }

        // 预览模式：选最大的 JPEG；缩略图模式：选最接近 100KB 的
        List<(long start, long end, long size)> candidates;
        if (preferLargest)
        {
            candidates = segments
                .Where(s => s.size > 10_000)
                .OrderByDescending(s => s.size)
                .ToList();
        }
        else
        {
            var targetSize = 100_000L;
            candidates = segments
                .Where(s => s.size > 10_000)
                .OrderBy(s => Math.Abs(s.size - targetSize))
                .ToList();
        }

        Debug.WriteLine($"ARW {Path.GetFileName(filePath)}: found {segments.Count} JPEG segments, " +
                        $"sizes: {string.Join(", ", segments.Select(s => $"{s.size / 1024}KB"))}");

        // 依次尝试解码，直到成功
        foreach (var seg in candidates)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"photocut_{Guid.NewGuid()}.jpg");
            try
            {
                ExtractSegment(filePath, seg, tempPath);
                var bitmap = await DecodeJpgBitmapAsync(tempPath, maxSize);
                if (bitmap != null)
                {
                    Debug.WriteLine($"Successfully decoded ARW preview: {seg.size / 1024}KB");
                    return bitmap;
                }
                Debug.WriteLine($"Decode failed for segment {seg.size / 1024}KB, trying next...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Segment decode error: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        Debug.WriteLine($"All decode attempts failed for: {filePath}");
        return CreatePlaceholderBitmap();
    }

    /// <summary>
    /// 流式扫描 ARW 文件，找到所有嵌入的 JPEG 段
    /// </summary>
    private Task<List<(long start, long end, long size)>> FindEmbeddedJpegsAsync(string arwPath)
    {
        return Task.Run(() =>
        {
            var segments = new List<(long start, long end, long size)>();

            try
            {
                using var fs = File.OpenRead(arwPath);
                int prev = fs.ReadByte();
                int curr;
                long pos = 1;
                long jpegStart = -1;

                while ((curr = fs.ReadByte()) != -1)
                {
                    if (prev == 0xFF && curr == 0xD8)
                    {
                        jpegStart = pos - 1;
                    }
                    else if (prev == 0xFF && curr == 0xD9 && jpegStart >= 0)
                    {
                        long jpegEnd = pos + 1;
                        long size = jpegEnd - jpegStart;
                        segments.Add((jpegStart, jpegEnd, size));
                        jpegStart = -1;
                    }
                    prev = curr;
                    pos++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Find JPEGs error: {ex.Message}");
            }

            return segments;
        });
    }

    /// <summary>
    /// 从 ARW 文件中提取指定位置的 JPEG 段到文件
    /// </summary>
    private void ExtractSegment(string arwPath, (long start, long end, long size) seg, string outputPath)
    {
        using var fs = new FileStream(arwPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = File.Create(outputPath);

        fs.Seek(seg.start, SeekOrigin.Begin);
        long remaining = seg.size;
        var buffer = new byte[81920];

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = fs.Read(buffer, 0, toRead);
            if (read == 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private SoftwareBitmap CreatePlaceholderBitmap()
    {
        var width = 200;
        var height = 200;
        var pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x66;
            pixels[i + 1] = 0x7E;
            pixels[i + 2] = 0xEA;
            pixels[i + 3] = 0xFF;
        }

        var softwareBitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            width, height,
            BitmapAlphaMode.Premultiplied);

        softwareBitmap.CopyFromBuffer(pixels.AsBuffer());
        return softwareBitmap;
    }

    /// <summary>
    /// 创建占位图 Source（必须在 UI 线程调用）
    /// </summary>
    public async Task<SoftwareBitmapSource> CreatePlaceholderSource()
    {
        var bitmap = CreatePlaceholderBitmap();
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }

    public void ClearCache()
    {
        _thumbnailCache.Clear();
    }
}
