using System.Diagnostics;
using System.Text;

namespace ShadOBS
{
    public static class Shell
    {
        public static int Run(string exe, string args, int timeoutMs, out string output)
        {
            var info = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var text = new StringBuilder();
            using (var process = new Process { StartInfo = info })
            {
                DataReceivedEventHandler collect = (_, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    lock (text)
                    {
                        text.AppendLine(e.Data);
                    }
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (System.Exception)
                    {
                    }
                    output = text.ToString();
                    return -1;
                }

                process.WaitForExit();
                output = text.ToString();
                return process.ExitCode;
            }
        }
    }
}
