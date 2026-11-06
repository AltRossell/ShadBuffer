using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ShadBuffer
{
    public sealed class DisplayInfo
    {
        public int Number { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string MonitorId { get; set; }
        public bool IsPrimary { get; set; }
    }

    public static class SystemInfo
    {
        private const int EnumCurrentSettings = -1;
        private const int AttachedToDesktop = 0x1;
        private const int PrimaryDevice = 0x4;
        private const uint GetDeviceInterfaceName = 1;
        private const int MegabytesPerSecond = 25;
        private const int MinimumMemoryMb = 512;
        private const int MaximumMemoryMb = 8192;
        private const double MemoryShare = 0.2;

        // real pixels of every monitor, not scaled by windows dpi
        public static List<DisplayInfo> AllDisplays()
        {
            var displays = new List<DisplayInfo>();
            var adapter = NewDevice();
            for (uint i = 0; EnumDisplayDevicesW(null, i, ref adapter, 0); i++)
            {
                if ((adapter.StateFlags & AttachedToDesktop) != 0)
                {
                    DisplayInfo display = Describe(adapter.DeviceName);
                    if (display != null)
                    {
                        display.Number = displays.Count + 1;
                        display.IsPrimary = (adapter.StateFlags & PrimaryDevice) != 0;
                        displays.Add(display);
                    }
                }
                adapter = NewDevice();
            }
            return displays;
        }

        public static DisplayInfo PrimaryDisplay()
        {
            DisplayInfo primary = AllDisplays().FirstOrDefault(d => d.IsPrimary);
            if (primary == null)
            {
                throw new SetupException("Couldn't find your main monitor.");
            }
            return primary;
        }

        // desktop capture sizes the canvas to the monitor it records so nothing gets scaled
        public static DisplayInfo CanvasFor(Settings settings)
        {
            if (settings.Capture == Settings.CaptureDesktop)
            {
                DisplayInfo chosen = AllDisplays().FirstOrDefault(d => d.MonitorId == settings.MonitorId);
                if (chosen != null)
                {
                    return chosen;
                }
            }
            return PrimaryDisplay();
        }

        // the buffer sits in ram the whole time so i cap it to a share of what the pc has
        public static int ReplayMemoryMb(int clipSeconds)
        {
            int wanted = clipSeconds * MegabytesPerSecond;
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref status))
            {
                wanted = Math.Min(wanted, (int)(status.TotalPhysical / 1048576.0 * MemoryShare));
            }
            return Math.Max(MinimumMemoryMb, Math.Min(wanted, MaximumMemoryMb));
        }

        private static DisplayInfo Describe(string adapterName)
        {
            var mode = new DevMode { Size = (short)Marshal.SizeOf(typeof(DevMode)) };
            if (!EnumDisplaySettingsW(adapterName, EnumCurrentSettings, ref mode))
            {
                return null;
            }

            var monitor = NewDevice();
            string monitorId = EnumDisplayDevicesW(adapterName, 0, ref monitor, GetDeviceInterfaceName) ? monitor.DeviceID : "";
            return new DisplayInfo { Width = mode.PelsWidth, Height = mode.PelsHeight, MonitorId = monitorId };
        }

        private static DisplayDevice NewDevice()
        {
            return new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice info, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode info);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public short SpecVersion;
            public short DriverVersion;
            public short Size;
            public short DriverExtra;
            public int Fields;
            public int PositionX;
            public int PositionY;
            public int DisplayOrientation;
            public int DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public short LogPixels;
            public int BitsPerPel;
            public int PelsWidth;
            public int PelsHeight;
            public int DisplayFlags;
            public int DisplayFrequency;
            public int ICMMethod;
            public int ICMIntent;
            public int MediaType;
            public int DitherType;
            public int Reserved1;
            public int Reserved2;
            public int PanningWidth;
            public int PanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }
    }
}
