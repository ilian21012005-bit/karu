using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Karu.App.Services;

/// <summary>
/// Job Windows : tous les processus assignés sont tués quand Karu se ferme
/// (évite les ffmpeg orphelins qui cassent le prochain démarrage).
/// </summary>
internal static class ChildProcessJob
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    private static readonly object Gate = new();
    private static IntPtr _job = IntPtr.Zero;

    public static void Assign(Process process)
    {
        try
        {
            lock (Gate)
            {
                EnsureJobUnlocked();
                if (_job == IntPtr.Zero || process.HasExited)
                {
                    return;
                }

                if (!AssignProcessToJobObject(_job, process.Handle))
                {
                    AppLog.WriteAlways("job assign failed err=" + Marshal.GetLastWin32Error());
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.WriteAlways("job assign: " + ex.Message);
        }
    }

    /// <summary>Tue les ffmpeg lancés depuis notre binaire (orphelins d'anciennes sessions).</summary>
    public static void KillStaleFrom(string ffmpegPath)
    {
        string ours;
        try
        {
            ours = Path.GetFullPath(ffmpegPath);
        }
        catch
        {
            return;
        }

        foreach (var proc in Process.GetProcessesByName("ffmpeg"))
        {
            try
            {
                var path = TryGetProcessPath(proc);
                if (path is null ||
                    !string.Equals(Path.GetFullPath(path), ours, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AppLog.WriteAlways("kill stale ffmpeg pid=" + proc.Id);
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                catch
                {
                    ForceKillPid(proc.Id);
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteAlways("kill stale: " + ex.Message);
            }
            finally
            {
                try { proc.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    private static string? TryGetProcessPath(Process proc)
    {
        try
        {
            var path = proc.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }
        catch
        {
            // MainModule souvent inaccessible → QueryFullProcessImageName
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, proc.Id);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size))
            {
                return null;
            }

            return sb.ToString();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void ForceKillPid(int pid)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /PID {pid}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            kill?.WaitForExit(3000);
        }
        catch
        {
            // ignore
        }
    }

    private static void EnsureJobUnlocked()
    {
        if (_job != IntPtr.Zero)
        {
            return;
        }

        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
        {
            return;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_job, 9, ptr, (uint)length))
            {
                CloseHandle(_job);
                _job = IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        StringBuilder lpExeName,
        ref int lpdwSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
