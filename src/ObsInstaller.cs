using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Web.Script.Serialization;

namespace ShadBuffer
{
    // only runs inside the elevated step
    public static class ObsInstaller
    {
        private const string WingetId = "OBSProject.OBSStudio";
        private const string ReleaseApi = "https://api.github.com/repos/obsproject/obs-studio/releases/latest";
        private const string Publisher = "OBS Project, LLC";

        public static void EnsureInstalled()
        {
            if (IsReady())
            {
                return;
            }
            if (!TryWinget() || !IsReady())
            {
                InstallFromRelease();
            }
            if (!IsReady())
            {
                throw new SetupException("OBS didn't install. You can get it from obsproject.com and run this again.");
            }
        }

        public static string DownloadInstaller(string folder)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "ShadBuffer-setup";
                string json = web.DownloadString(ReleaseApi);
                var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var assets = ((IEnumerable)release["assets"]).Cast<Dictionary<string, object>>();
                var asset = assets.FirstOrDefault(a => IsWindowsInstaller((string)a["name"]));
                if (asset == null)
                {
                    throw new SetupException("Couldn't find the OBS installer in its latest release.");
                }

                string target = Path.Combine(folder, (string)asset["name"]);
                web.Headers[HttpRequestHeader.UserAgent] = "ShadBuffer-setup";
                web.DownloadFile((string)asset["browser_download_url"], target);
                return target;
            }
        }

        public static bool IsWindowsInstaller(string name)
        {
            return name.EndsWith("Installer.exe", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("arm64", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // i won't run a downloaded installer as admin unless obs really signed it
        public static bool IsSignedByObs(string file)
        {
            if (!HasTrustedSignature(file))
            {
                return false;
            }
            try
            {
                var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
                return certificate.GetNameInfo(X509NameType.SimpleName, false) == Publisher;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsReady()
        {
            string root = Obs.FindInstalledRoot();
            return root != null && Obs.IsNewEnough(root);
        }

        private static bool TryWinget()
        {
            string args = $"install --id {WingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            try
            {
                return Shell.Run("winget", args, 600000, out _) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void InstallFromRelease()
        {
            string folder = Path.Combine(Path.GetTempPath(), "shadbuffer-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            try
            {
                string installer = DownloadInstaller(folder);
                if (!IsSignedByObs(installer))
                {
                    throw new SetupException("The downloaded OBS installer wasn't signed by OBS, so it wasn't run.");
                }
                Shell.Run(installer, "/S", 600000, out _);
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private static bool HasTrustedSignature(string file)
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                FilePath = file,
            };
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(fileInfo, filePointer, false);
                var data = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    RevocationChecks = 0,
                    UnionChoice = 1,
                    FileInfo = filePointer,
                    StateAction = 0,
                    ProviderFlags = 0x00000080,
                };
                var verifyAction = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1), ref verifyAction, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(filePointer, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(filePointer);
            }
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallback;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }
    }
}
