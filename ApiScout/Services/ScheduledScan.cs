using System.Diagnostics;
using System.Security;
using System.Text;

namespace ApiScout.Services;

/// <summary>
/// The "also when API Scout is closed" option: a per-user Windows scheduled task that runs "ApiScout.exe --scan".
/// No admin rights needed; it only runs while the user is signed in, so the notification can be shown.
/// </summary>
public static class ScheduledScan
{
    public const string TaskName = "ApiScout background scan";

    public static bool IsRegistered() => Run($"/Query /TN \"{TaskName}\"").Code == 0;

    public static (bool Ok, string Message) Register(bool daily)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APISCOUT_DATA")))
            return (false, "Not registered: this copy runs on a test data folder (APISCOUT_DATA).");
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe)) return (false, "Could not work out where ApiScout.exe is.");

        var xml = Path.Combine(Path.GetTempPath(), "apiscout-task.xml");
        try
        {
            File.WriteAllText(xml, BuildXml(exe, daily), Encoding.Unicode); // schtasks wants UTF-16
            var (code, output) = Run($"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F");
            return code == 0
                ? (true, $"Windows will now re-scan {(daily ? "every day" : "every Monday")} at 09:00 (or as soon as the PC is next on) and notify you about new APIs.")
                : (false, "Windows refused the scheduled task: " + output.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return (false, "Could not create the scheduled task: " + ex.Message); }
        finally { try { File.Delete(xml); } catch (IOException) { } }
    }

    public static (bool Ok, string Message) Unregister()
    {
        if (!IsRegistered()) return (true, "Background re-scan is off.");
        var (code, output) = Run($"/Delete /TN \"{TaskName}\" /F");
        return code == 0 ? (true, "Background re-scan is off - the Windows scheduled task was removed.") : (false, "Could not remove the scheduled task: " + output.Trim());
    }

    internal static string BuildXml(string exe, bool daily) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Re-scans the free API directories for API Scout and shows a notification when new APIs appear. Turn it off in API Scout (Sources).</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2026-01-05T09:00:00</StartBoundary>
              <Enabled>true</Enabled>
              {(daily ? "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>" : "<ScheduleByWeek><DaysOfWeek><Monday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>")}
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
            <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exe)}</Command>
              <Arguments>--scan</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    private static (int Code, string Output) Run(string arguments)
    {
        Process? started;
        try
        {
            started = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
        }
        catch (System.ComponentModel.Win32Exception ex) { return (-1, "Could not start schtasks.exe: " + ex.Message); }
        if (started is null) return (-1, "Could not start schtasks.exe.");
        using var p = started;
        // read both pipes in the background: ReadToEnd here would wait for the process, and the 15 seconds below would never apply
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return (-1, "Windows Task Scheduler did not answer within 15 seconds.");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
