using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ShadBuffer
{
    public sealed class AudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public static class AudioDevices
    {
        private const int Render = 0;
        private const int Capture = 1;
        private const int StateActive = 1;
        private const int StoreRead = 0;
        private const ushort VtLpwstr = 31;

        public static List<AudioDevice> Outputs()
        {
            return List(Render);
        }

        public static List<AudioDevice> Microphones()
        {
            return List(Capture);
        }

        // the ids here are the same ones obs saves as device_id
        private static List<AudioDevice> List(int flow)
        {
            var devices = new List<AudioDevice>();
            try
            {
                var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, StateActive, out IDeviceCollection collection));
                Marshal.ThrowExceptionForHR(collection.GetCount(out int count));
                for (int i = 0; i < count; i++)
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out IDevice device));
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    devices.Add(new AudioDevice { Id = id, Name = FriendlyName(device) ?? id });
                }
            }
            catch (COMException)
            {
            }
            catch (InvalidCastException)
            {
            }
            return devices;
        }

        private static string FriendlyName(IDevice device)
        {
            if (device.OpenPropertyStore(StoreRead, out IPropertyStore store) != 0)
            {
                return null;
            }
            var key = new PropertyKey
            {
                Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                Id = 14,
            };
            if (store.GetValue(ref key, out PropVariant value) != 0 || value.Type != VtLpwstr)
            {
                return null;
            }
            string name = Marshal.PtrToStringUni(value.Pointer);
            PropVariantClear(ref value);
            return name;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class DeviceEnumerator
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int flow, int stateMask, out IDeviceCollection devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceCollection
        {
            [PreserveSig]
            int GetCount(out int count);

            [PreserveSig]
            int Item(int index, out IDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

            [PreserveSig]
            int OpenPropertyStore(int access, out IPropertyStore store);

            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out int count);

            [PreserveSig]
            int GetAt(int index, out PropertyKey key);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public int Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort Type;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public IntPtr Pointer;
            public IntPtr Padding;
        }
    }
}
