using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>
/// For native installs: starts the Syncplay server and restarts it if it exits.
/// Does nothing when Tlp:Syncplay:ServerCommand is empty (e.g. Docker, where Syncplay
/// has its own container).
/// </summary>
public sealed class SyncplayProcess(IOptions<TlpOptions> options, ILogger<SyncplayProcess> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sp = options.Value.Syncplay;
        if (string.IsNullOrWhiteSpace(sp.ServerCommand)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var psi = new ProcessStartInfo(sp.ServerCommand)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(sp.Port.ToString());
            if (!string.IsNullOrEmpty(sp.ServerArguments))
                foreach (var a in sp.ServerArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a);
            if (!string.IsNullOrEmpty(sp.Password)) psi.Environment["SYNCPLAY_PASSWORD"] = sp.Password;

            try
            {
                using var process = Process.Start(psi)!;
                log.LogInformation("Started Syncplay server (pid {Pid}) on port {Port}", process.Id, sp.Port);
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.LogInformation("syncplay: {Line}", e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.LogWarning("syncplay: {Line}", e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    await process.WaitForExitAsync(stoppingToken);
                    log.LogWarning("Syncplay server exited with code {Code}; restarting in 5s", process.ExitCode);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    return;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                log.LogError(ex, "Could not start Syncplay server '{Command}'; retrying in 5s", sp.ServerCommand);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
