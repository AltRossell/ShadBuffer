using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ShadBuffer
{
    public enum LibraryKind
    {
        Clips,
        Recordings,
    }

    public sealed class LibraryFile
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public DateTime Modified { get; set; }
        public long Size { get; set; }
    }

    public sealed class LibraryFolder
    {
        public string Name { get; set; }
        public List<LibraryFile> Files { get; } = new List<LibraryFile>();
        public DateTime Modified => Files.Max(file => file.Modified);
        public long Size => Files.Sum(file => file.Size);
    }

    public static class Library
    {
        public const string LooseFolderName = "Not sorted";

        private const string DesktopFolderName = "Desktop";
        private const int WritingGraceSeconds = 5;

        private static readonly HashSet<string> VideoTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".mov", ".webm", ".flv", ".ts",
        };

        private static readonly Color DesktopBadge = Color.FromArgb(0x52, 0x52, 0x5B);
        private static readonly Color[] Badges =
        {
            Color.FromArgb(0x0F, 0x76, 0x6E),
            Color.FromArgb(0xB4, 0x53, 0x09),
            Color.FromArgb(0xBE, 0x18, 0x5D),
            Color.FromArgb(0x1D, 0x4E, 0xD8),
            Color.FromArgb(0x15, 0x80, 0x3D),
            Color.FromArgb(0xB9, 0x1C, 0x1C),
            Color.FromArgb(0x0E, 0x74, 0x90),
            Color.FromArgb(0xA1, 0x62, 0x07),
        };

        // the recording obs is still writing can't play yet, so it stays hidden until it's done
        public static List<LibraryFolder> Scan(string root, DateTime? writingSinceUtc)
        {
            var folders = new List<LibraryFolder>();
            if (!Directory.Exists(root))
            {
                return folders;
            }
            try
            {
                foreach (string folder in Directory.EnumerateDirectories(root))
                {
                    AddIfNotEmpty(folders, ReadFolder(System.IO.Path.GetFileName(folder), folder, null));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            AddIfNotEmpty(folders, ReadFolder(LooseFolderName, root, writingSinceUtc));
            return folders;
        }

        public static List<LibraryFolder> Sorted(IEnumerable<LibraryFolder> folders, bool bySize)
        {
            return (bySize ? folders.OrderByDescending(folder => folder.Size) : folders.OrderByDescending(folder => folder.Modified)).ToList();
        }

        public static List<LibraryFile> Sorted(LibraryFolder folder, bool bySize)
        {
            return (bySize ? folder.Files.OrderByDescending(file => file.Size) : folder.Files.OrderByDescending(file => file.Modified)).ToList();
        }

        public static string When(DateTime time)
        {
            DateTime today = DateTime.Today;
            if (time.Date == today)
            {
                return "Today " + time.ToString("HH:mm");
            }
            if (time.Date == today.AddDays(-1))
            {
                return "Yesterday " + time.ToString("HH:mm");
            }
            return time.Year == today.Year ? time.ToString("d MMM HH:mm") : time.ToString("d MMM yyyy");
        }

        public static string SizeText(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024)
            {
                return (mb / 1024).ToString("0.0") + " GB";
            }
            return mb >= 1 ? Math.Round(mb) + " MB" : Math.Max(1, (int)Math.Round(mb * 1024)) + " KB";
        }

        public static string LengthText(TimeSpan length)
        {
            return length.TotalHours >= 1
                ? (int)length.TotalHours + length.ToString(@"\:mm\:ss")
                : (int)length.TotalMinutes + length.ToString(@"\:ss");
        }

        public static string Plural(int count, string word)
        {
            return count + " " + word + (count == 1 ? "" : "s");
        }

        public static string Monogram(string name)
        {
            string[] words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
            }
            return name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
        }

        // same game keeps the same colour every time, string.GetHashCode isn't stable across runs
        public static Color BadgeColor(string name)
        {
            if (name == DesktopFolderName || name == LooseFolderName)
            {
                return DesktopBadge;
            }
            uint hash = 2166136261;
            foreach (char letter in name)
            {
                hash = (hash ^ letter) * 16777619;
            }
            return Badges[hash % Badges.Length];
        }

        private static void AddIfNotEmpty(List<LibraryFolder> folders, LibraryFolder folder)
        {
            if (folder.Files.Count > 0)
            {
                folders.Add(folder);
            }
        }

        private static LibraryFolder ReadFolder(string name, string path, DateTime? writingSinceUtc)
        {
            var folder = new LibraryFolder { Name = name };
            try
            {
                foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles())
                {
                    if (!VideoTypes.Contains(file.Extension) || file.Length == 0)
                    {
                        continue;
                    }
                    if (writingSinceUtc != null && file.CreationTimeUtc >= writingSinceUtc.Value.AddSeconds(-WritingGraceSeconds))
                    {
                        continue;
                    }
                    folder.Files.Add(new LibraryFile
                    {
                        Path = file.FullName,
                        Name = file.Name,
                        Modified = file.LastWriteTime,
                        Size = file.Length,
                    });
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return folder;
        }
    }
}
