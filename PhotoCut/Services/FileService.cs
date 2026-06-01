using System.Diagnostics;
using System.Runtime.InteropServices;
using PhotoCut.Models;

namespace PhotoCut.Services;

public class FileService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".arw"
    };

    public async Task<List<PhotoItem>> ScanFolderAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            var photos = new List<PhotoItem>();
            var directory = new DirectoryInfo(folderPath);

            if (!directory.Exists) return photos;

            var files = directory.GetFiles()
                .Where(f => SupportedExtensions.Contains(f.Extension))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            var fileGroups = files
                .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in fileGroups)
            {
                var groupFiles = group.ToList();
                var hasJpg = groupFiles.Any(f => IsJpg(f.Extension));
                var hasArw = groupFiles.Any(f => IsArw(f.Extension));

                foreach (var file in groupFiles)
                {
                    var photo = new PhotoItem
                    {
                        FilePath = file.FullName,
                        FileName = file.Name,
                        Extension = file.Extension.ToLowerInvariant(),
                        DateModified = file.LastWriteTime,
                        FileSize = file.Length,
                        HasPairedFile = (hasJpg && hasArw) && groupFiles.Count > 1
                    };

                    if (photo.HasPairedFile)
                    {
                        var paired = groupFiles.FirstOrDefault(f => f.FullName != file.FullName);
                        photo.PairedFilePath = paired?.FullName;
                    }

                    photos.Add(photo);
                }
            }

            return photos.OrderByDescending(p => p.DateModified).ToList();
        });
    }

    public async Task<List<string>> DeleteFilesAsync(List<string> filePaths, bool useRecycleBin)
    {
        var deleted = new List<string>();

        foreach (var filePath in filePaths)
        {
            try
            {
                if (!File.Exists(filePath)) continue;

                if (useRecycleBin)
                {
                    await DeleteToRecycleBinAsync(filePath);
                }
                else
                {
                    File.Delete(filePath);
                }

                deleted.Add(filePath);
                Debug.WriteLine($"Deleted: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete {filePath}: {ex.Message}");
            }
        }

        return deleted;
    }

    private Task DeleteToRecycleBinAsync(string filePath)
    {
        return Task.Run(() =>
        {
            var shfop = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };

            SHFileOperation(ref shfop);
        });
    }

    private static bool IsJpg(string ext)
    {
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArw(string ext)
    {
        return ext.Equals(".arw", StringComparison.OrdinalIgnoreCase);
    }

    #region Shell API for Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public short fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    private const int FO_DELETE = 3;
    private const short FOF_ALLOWUNDO = 0x40;
    private const short FOF_NOCONFIRMATION = 0x10;
    private const short FOF_SILENT = 0x04;

    #endregion
}
