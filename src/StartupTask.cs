using System;
using System.IO;
using System.Security;
using System.Text;

namespace ShadOBS
{
    public static class StartupTask
    {
        public const string Name = "ShadOBS";
        public const string TrayName = "ShadOBS Tray";

        // no trigger, the tray helper runs this at login so it can clear the crash marker first
        public static string BuildXml(string sid, string obsRoot, bool runAsAdmin)
        {
            string exe = SecurityElement.Escape(Obs.ExePath(obsRoot));
            string folder = SecurityElement.Escape(Path.GetDirectoryName(Obs.ExePath(obsRoot)) + "\\");
            string args = SecurityElement.Escape(Obs.LaunchArgs);
            string runLevel = runAsAdmin ? "HighestAvailable" : "LeastPrivilege";

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts OBS in the tray with the replay buffer running</Description>
    <URI>\{Name}</URI>
  </RegistrationInfo>
  <Triggers />
  <Principals>
    <Principal id=""Author"">
      <UserId>{sid}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>{runLevel}</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>{args}</Arguments>
      <WorkingDirectory>{folder}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
";
        }

        public static void Register(string sid, string obsRoot, bool runAsAdmin)
        {
            string file = Path.Combine(Path.GetTempPath(), "shadobs-task-" + Guid.NewGuid() + ".xml");
            File.WriteAllText(file, BuildXml(sid, obsRoot, runAsAdmin), Encoding.Unicode);
            try
            {
                int code = Shell.Run("schtasks", $"/create /tn \"{Name}\" /xml \"{file}\" /f", 30000, out string output);
                if (code != 0)
                {
                    throw new SetupException("Couldn't create the startup task: " + output.Trim());
                }
            }
            finally
            {
                File.Delete(file);
            }
        }

        public static void Delete()
        {
            Shell.Run("schtasks", $"/delete /tn \"{Name}\" /f", 30000, out _);
        }

        // windows holds startup entries back for over a minute, a login task runs straight away
        public static string BuildTrayXml(string sid, string exe)
        {
            string command = SecurityElement.Escape(exe);
            string folder = SecurityElement.Escape(Path.GetDirectoryName(exe));

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts the ShadOBS tray helper at login</Description>
    <URI>\{TrayName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{sid}</UserId>
      <Delay>PT5S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{sid}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{command}</Command>
      <Arguments>--tray --login</Arguments>
      <WorkingDirectory>{folder}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
";
        }

        public static bool RegisterTray(string sid, string exe)
        {
            string file = Path.Combine(Path.GetTempPath(), "shadobs-tray-" + Guid.NewGuid() + ".xml");
            File.WriteAllText(file, BuildTrayXml(sid, exe), Encoding.Unicode);
            try
            {
                return Shell.Run("schtasks", $"/create /tn \"{TrayName}\" /xml \"{file}\" /f", 30000, out _) == 0;
            }
            finally
            {
                File.Delete(file);
            }
        }

        public static void DeleteTray()
        {
            Shell.Run("schtasks", $"/delete /tn \"{TrayName}\" /f", 30000, out _);
        }

        public static bool Start()
        {
            return Shell.Run("schtasks", $"/run /tn \"{Name}\"", 30000, out _) == 0;
        }
    }
}
