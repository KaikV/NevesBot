using KBot.App.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace KBot.App.Services;

public sealed class GameProcessWatcher
{
    public (Process Process, nint Handle, string Path)? FindRunning(GameInstallation client, LauncherSession? launcher = null)
    {
        client.Normalize();
        var ranked = new List<(Process Process, nint Handle, string Path, int Score)>();
        var parents = launcher is null ? new Dictionary<int, int>() : GetParents();
        var knownPaths = FindKnownExecutables(client);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited) { process.Dispose(); continue; }
                string? path;
                var pathVerified = true;
                try { path = process.MainModule?.FileName; }
                catch (Win32Exception) { path = null; pathVerified = false; }
                catch (UnauthorizedAccessException) { path = null; pathVerified = false; }
                var processName = process.ProcessName + ".exe";
                if (pathVerified)
                {
                    if (string.IsNullOrWhiteSpace(path) || !InsideInstallation(path, client.InstallDirectory))
                    { process.Dispose(); continue; }
                }
                else if (!knownPaths.TryGetValue(processName, out path))
                { process.Dispose(); continue; }
                var name = Path.GetFileName(path);
                // A selected executable may itself be the game. Keep an actual launcher
                // separate, but accept a directly selected client once its window exists.
                var selectedClient = string.Equals(path, client.LauncherPath, StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("launcher", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("patcher", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("updater", StringComparison.OrdinalIgnoreCase);
                if (string.Equals(path, client.LauncherPath, StringComparison.OrdinalIgnoreCase) && !selectedClient)
                { process.Dispose(); continue; }
                var known = knownPaths.ContainsKey(name);
                var learned = string.Equals(path, client.LastGameExecutable, StringComparison.OrdinalIgnoreCase);
                var likely = IsLikelyGameName(name);
                var child = launcher is not null && parents.TryGetValue(process.Id, out var parent) && parent == launcher.LauncherPid;
                var recent = launcher is not null && process.StartTime >= launcher.StartedAt.AddSeconds(-3);
                if (!(selectedClient || known || learned || (pathVerified && likely) || (pathVerified && child && recent)))
                { process.Dispose(); continue; }
                process.Refresh();
                var handle = WindowCaptureService.FindWindowForProcess(process.Id, process.MainWindowHandle);
                if (handle == 0) { process.Dispose(); continue; }
                var score = (pathVerified ? 200 : 0) + (learned ? 100 : 0) + (known ? 50 : 0) +
                    (child ? 25 : 0) + (recent ? 10 : 0) + (name.Contains("_dx", StringComparison.OrdinalIgnoreCase) ? 5 : 0);
                ranked.Add((process, handle, path!, score));
            }
            catch (Win32Exception) { process.Dispose(); }
            catch (InvalidOperationException) { process.Dispose(); }
            catch (UnauthorizedAccessException) { process.Dispose(); }
        }
        var best = ranked.OrderByDescending(x => x.Score).FirstOrDefault();
        foreach (var candidate in ranked)
            if (candidate.Process != best.Process) candidate.Process.Dispose();
        return best.Process is null ? null : (best.Process, best.Handle, best.Path);
    }

    private static Dictionary<string, string> FindKnownExecutables(GameInstallation client)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var selectedName = Path.GetFileName(client.LauncherPath);
        if (File.Exists(client.LauncherPath) &&
            !selectedName.Contains("launcher", StringComparison.OrdinalIgnoreCase) &&
            !selectedName.Contains("patcher", StringComparison.OrdinalIgnoreCase) &&
            !selectedName.Contains("updater", StringComparison.OrdinalIgnoreCase))
            paths[selectedName] = client.LauncherPath;
        if (!string.IsNullOrWhiteSpace(client.LastGameExecutable) && File.Exists(client.LastGameExecutable))
            paths[Path.GetFileName(client.LastGameExecutable)] = client.LastGameExecutable;
        var directories = new[] { client.InstallDirectory, Path.Combine(client.InstallDirectory, "bin"),
            Path.Combine(client.InstallDirectory, "client"), Path.Combine(client.InstallDirectory, "data") };
        foreach (var directory in directories.Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(path);
                    if (string.Equals(path, client.LauncherPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (client.GameExecutableNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                        IsLikelyGameName(name)) paths.TryAdd(name, path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return paths;
    }

    private static bool IsLikelyGameName(string executableName) =>
        executableName.Contains("client", StringComparison.OrdinalIgnoreCase) ||
        executableName.Contains("_dx", StringComparison.OrdinalIgnoreCase) ||
        executableName.Contains("_gl", StringComparison.OrdinalIgnoreCase) ||
        executableName.Contains("game", StringComparison.OrdinalIgnoreCase) ||
        executableName.StartsWith("otclient", StringComparison.OrdinalIgnoreCase);

    public async Task<(Process Process, nint Handle, string Path)> WaitForGameAsync(
        GameInstallation client, LauncherSession launcher, CancellationToken cancellationToken)
    {
        var launcherExitLogged = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = FindRunning(client, launcher);
            if (found.HasValue) return found.Value;
            if (!launcherExitLogged && !IsLauncherRunning(launcher))
            {
                launcherExitLogged = true;
                Trace.WriteLine($"[Handoff] Launcher pid={launcher.LauncherPid} closed before game HWND; continuing to watch the game process.");
            }
            await Task.Delay(500, cancellationToken);
        }
    }

    private static bool IsLauncherRunning(LauncherSession launcher)
    {
        try
        {
            using var process = Process.GetProcessById(launcher.LauncherPid);
            return !process.HasExited && process.ProcessName.Equals(
                Path.GetFileNameWithoutExtension(launcher.LauncherPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static bool InsideInstallation(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<int, int> GetParents()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
                do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
                while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
