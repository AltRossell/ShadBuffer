using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ShadBuffer
{
    public sealed class MediaInfo
    {
        public Bitmap Thumb { get; set; }
        public TimeSpan? Length { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }

        public string VideoText()
        {
            if (Height <= 0)
            {
                return "-";
            }
            return Fps > 0 ? Height + "p, " + Math.Round(Fps) + " fps" : Height + "p";
        }
    }

    // windows already knows how to read thumbnails and lengths, and it caches them too
    public sealed class MediaCache : IDisposable
    {
        private const int MaxKept = 150;
        private const int ThumbWidth = 320;
        private const int ThumbHeight = 180;

        // resize to fit, and only a real thumbnail, otherwise it hands back a generic file icon
        private const int ThumbnailOnly = 0x8;
        private const int BestEffort = 0x40 | 0x10;
        private const ushort TypeUInt32 = 19;
        private const ushort TypeUInt64 = 21;
        private const double FrameRateUnits = 1000.0;

        private static readonly Guid ImageFactoryId = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        private static readonly Guid PropertyStoreId = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        private static readonly Guid MediaProperties = new Guid("64440490-4C8B-11D1-8B70-080036B11A03");
        private static readonly Guid VideoProperties = new Guid("64440491-4C8B-11D1-8B70-080036B11A03");

        private readonly object gate = new object();
        private readonly Dictionary<string, MediaInfo> done = new Dictionary<string, MediaInfo>();
        private readonly Queue<string> doneOrder = new Queue<string>();
        private readonly HashSet<string> waiting = new HashSet<string>();
        private readonly Stack<string> queue = new Stack<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly SynchronizationContext ui = SynchronizationContext.Current;
        private readonly Thread worker;
        private bool stopping;

        public event Action Updated;

        public MediaCache()
        {
            worker = new Thread(Work) { IsBackground = true, Name = "media" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        // null until it's loaded, Updated fires when there's more to draw
        public MediaInfo Get(string path)
        {
            lock (gate)
            {
                while (doneOrder.Count > MaxKept)
                {
                    string oldest = doneOrder.Dequeue();
                    if (done.TryGetValue(oldest, out MediaInfo old))
                    {
                        old.Thumb?.Dispose();
                        done.Remove(oldest);
                    }
                }
                if (done.TryGetValue(path, out MediaInfo info))
                {
                    return info;
                }
                if (waiting.Add(path))
                {
                    queue.Push(path);
                    wake.Set();
                }
                return null;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                stopping = true;
                foreach (MediaInfo info in done.Values)
                {
                    info.Thumb?.Dispose();
                }
                done.Clear();
                queue.Clear();
            }
            wake.Set();
        }

        private void Work()
        {
            while (true)
            {
                wake.WaitOne();
                while (true)
                {
                    string path;
                    lock (gate)
                    {
                        if (stopping)
                        {
                            wake.Dispose();
                            return;
                        }
                        if (queue.Count == 0)
                        {
                            break;
                        }
                        path = queue.Pop();
                    }

                    MediaInfo info = Load(path);
                    lock (gate)
                    {
                        if (stopping)
                        {
                            info.Thumb?.Dispose();
                            continue;
                        }
                        done[path] = info;
                        doneOrder.Enqueue(path);
                        waiting.Remove(path);
                    }
                    ui?.Post(_ => Updated?.Invoke(), null);
                }
            }
        }

        private static MediaInfo Load(string path)
        {
            var info = new MediaInfo();
            if (!File.Exists(path))
            {
                return info;
            }
            info.Thumb = ReadThumb(path);
            ReadProperties(path, info);
            return info;
        }

        private static Bitmap ReadThumb(string path)
        {
            IShellItemImageFactory factory = null;
            try
            {
                Guid id = ImageFactoryId;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out factory);
                var size = new NativeSize { Width = Ui.S(ThumbWidth), Height = Ui.S(ThumbHeight) };
                if (factory.GetImage(size, ThumbnailOnly, out IntPtr handle) != 0)
                {
                    return null;
                }
                try
                {
                    using (Bitmap raw = Image.FromHbitmap(handle))
                    {
                        return new Bitmap(raw);
                    }
                }
                finally
                {
                    DeleteObject(handle);
                }
            }
            catch (Exception e) when (e is COMException || e is ExternalException || e is ArgumentException || e is FileNotFoundException)
            {
                return null;
            }
            finally
            {
                if (factory != null)
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }

        private static void ReadProperties(string path, MediaInfo info)
        {
            IPropertyStore store = null;
            try
            {
                Guid id = PropertyStoreId;
                SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, BestEffort, ref id, out store);

                ulong duration = ReadNumber(store, MediaProperties, 3);
                if (duration > 0)
                {
                    info.Length = TimeSpan.FromTicks((long)duration);
                }
                info.Height = (int)ReadNumber(store, VideoProperties, 4);
                info.Fps = ReadNumber(store, VideoProperties, 6) / FrameRateUnits;
            }
            catch (Exception e) when (e is COMException || e is FileNotFoundException)
            {
            }
            finally
            {
                if (store != null)
                {
                    Marshal.ReleaseComObject(store);
                }
            }
        }

        private static ulong ReadNumber(IPropertyStore store, Guid format, int id)
        {
            var key = new PropertyKey { Format = format, Id = id };
            if (store.GetValue(ref key, out PropVariant value) != 0)
            {
                return 0;
            }
            try
            {
                if (value.Type == TypeUInt64)
                {
                    return value.Large;
                }
                return value.Type == TypeUInt32 ? value.Small : 0;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid Format;
            public int Id;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public ulong Large;
            [FieldOffset(8)] public uint Small;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, int flags, out IntPtr bitmap);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint count);

            [PreserveSig]
            int GetAt(uint index, out PropertyKey key);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItemImageFactory item);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr context, int flags, ref Guid id, out IPropertyStore store);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
