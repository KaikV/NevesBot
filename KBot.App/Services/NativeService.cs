using KBot.App.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KBot.App.Services
{
    public class NativeService : IDisposable
    {
        private const string PipeName = "KBot.NativePipe.CharacterV3";
        private Process? _ownedCore;
        public string? LastStatusError { get; private set; }
        public string? LastProcessName { get; private set; }

        public string StartCore()
        {
            if (_ownedCore is { HasExited: false }) return "Núcleo já iniciado pelo KBot.";

            try
            {
                try
                {
                    using var probe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                    probe.Connect(150);
                    return "Núcleo já em execução. Aguarde a próxima verificação.";
                }
                catch (TimeoutException) { }
                catch (IOException) { }
                var executable = FindCoreExecutable();
                if (executable is null) return "KBot.Native.exe não foi encontrado. Compile a solução KBot.sln.";
                _ownedCore?.Dispose();
                _ownedCore = Process.Start(new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                return _ownedCore is null ? "Não foi possível iniciar o núcleo." : "Núcleo iniciado. Aguardando resposta...";
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
            {
                return $"Não foi possível iniciar o núcleo: {ex.Message}";
            }
        }

        private static string? FindCoreExecutable()
        {
            using var resource = typeof(NativeService).Assembly.GetManifestResourceStream("KBot.App.Native.KBot.Native.exe");
            if (resource is not null)
            {
                using var bytes = new MemoryStream();
                resource.CopyTo(bytes);
                var image = bytes.ToArray();
                var hash = Convert.ToHexString(SHA256.HashData(image))[..16];
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KBot", "core", hash);
                var embeddedPath = Path.Combine(directory, "KBot.Native.exe");
                Directory.CreateDirectory(directory);
                if (!File.Exists(embeddedPath) || !SHA256.HashData(File.ReadAllBytes(embeddedPath)).SequenceEqual(SHA256.HashData(image)))
                {
                    var temporaryPath = Path.Combine(directory, $"KBot.Native.{Guid.NewGuid():N}.tmp");
                    try
                    {
                        File.WriteAllBytes(temporaryPath, image);
                        File.Move(temporaryPath, embeddedPath, overwrite: true);
                    }
                    finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                }
                return embeddedPath;
            }

            var local = Path.Combine(AppContext.BaseDirectory, "KBot.Native.exe");
            if (File.Exists(local)) return local;

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (!Directory.Exists(Path.Combine(directory.FullName, "KBot.Native"))) continue;
                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var candidate = Path.Combine(directory.FullName, "x64", configuration, "KBot.Native.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                break;
            }
            return null;
        }

        public void Dispose()
        {
            if (_ownedCore is null) return;
            try
            {
                if (!_ownedCore.HasExited) _ownedCore.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            finally
            {
                _ownedCore.Dispose();
                _ownedCore = null;
            }
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(1000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                await writer.WriteLineAsync("PING");
                var resp = await reader.ReadLineAsync();
                return resp?.Trim() == "PONG";
            }
            catch (IOException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public async Task<NativeStatus?> GetStatusAsync(CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(800));
            var timeoutToken = timeoutSource.Token;

            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(timeoutToken);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                await writer.WriteLineAsync("GET_STATUS");
                var resp = await reader.ReadLineAsync(timeoutToken);
                if (string.IsNullOrWhiteSpace(resp))
                {
                    LastStatusError = "Resposta vazia do núcleo.";
                    return null;
                }
                var status = JsonSerializer.Deserialize<NativeStatus>(resp);
                LastStatusError = status is null ? "Status vazio do núcleo." : null;
                if (status is { ProcessName.Length: > 0 }) LastProcessName = status.ProcessName;
                return status;
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException or ObjectDisposedException or JsonException)
            {
                LastStatusError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        public async Task<bool> SendKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            var normalized = key.Trim().ToUpperInvariant();
            if (normalized is not ("UP" or "DOWN" or "LEFT" or "RIGHT" or "W" or "A" or "S" or "D"))
                return false;

            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(800));
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(timeoutSource.Token);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                await writer.WriteLineAsync($"SEND_KEY {normalized}");
                return (await reader.ReadLineAsync(timeoutSource.Token))?.Trim() == "KEY_SENT";
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                LastStatusError = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> AttachGameAsync(int pid, string executableName, CancellationToken cancellationToken)
        {
            if (pid <= 0 || string.IsNullOrWhiteSpace(executableName) || executableName.Any(char.IsWhiteSpace)) return false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await SendCommandAsync($"ATTACH_PID {pid} {executableName}", cancellationToken) == "ATTACHED") return true;
                await Task.Delay(300, cancellationToken);
            }
            return false;
        }

        public Task<string?> DetachGameAsync(CancellationToken cancellationToken) =>
            SendCommandAsync("DETACH_PID", cancellationToken);

        // Offset-hunting helpers. Start a range around the confirmed position
        // anchor, then pull it in chunks (the pipe frame is 4KB). The response
        // lines are raw JSON; callers parse the hex/anchor fields.
        public Task<string?> StartMemoryDumpAsync(long offsetFromAnchor, int byteCount, CancellationToken cancellationToken = default) =>
            SendCommandAsync($"DUMP_START {offsetFromAnchor} {byteCount}", cancellationToken);

        public Task<string?> GetMemoryDumpChunkAsync(int chunkBytes = 4096, CancellationToken cancellationToken = default) =>
            SendCommandAsync($"DUMP_CHUNK {chunkBytes}", cancellationToken);

        public Task<string?> ResetMemoryDumpAsync(CancellationToken cancellationToken = default) =>
            SendCommandAsync("DUMP_RESET", cancellationToken);

        // Applies a hunted position offset at runtime (offset FROM the module
        // base, e.g. 0x37454E0). Returns raw JSON {ok,offset,message}.
        public Task<string?> SetPositionOffsetAsync(long offsetFromBase, CancellationToken cancellationToken = default) =>
            SendCommandAsync($"SET_POSITION_OFFSET {offsetFromBase:X}", cancellationToken);

        // Drops the runtime override and returns to the compiled-in DX/GL default.
        public Task<string?> ClearPositionOffsetAsync(CancellationToken cancellationToken = default) =>
            SendCommandAsync("CLEAR_POSITION_OFFSET", cancellationToken);

        // Reports whether a custom offset is active and the currently-active offset.
        public Task<string?> GetPositionOffsetStatusAsync(CancellationToken cancellationToken = default) =>
            SendCommandAsync("GET_OFFSET_STATUS", cancellationToken);

        // Scans the whole main module for an exact adjacent int32 triple (x,y,z)
        // - the minimap position. Returns raw JSON with a "candidates" array of
        // 0x-offsets from the module base. Parse on the caller side. Uses a long
        // timeout: a full-module scan touches many megabytes of memory.
        public async Task<string?> ScanForPositionAsync(int x, int y, int z, CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(timeout.Token);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
                await writer.WriteLineAsync($"SCAN_POSITION {x} {y} {z}");
                return await reader.ReadLineAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static async Task<string?> SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(timeout.Token);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
                await writer.WriteLineAsync(command);
                return await reader.ReadLineAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
