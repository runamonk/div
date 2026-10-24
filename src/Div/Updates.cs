using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using Div.Core;

namespace Div;

// Looks for a newer release on GitHub, downloads it, and hands it to DivUpdater.exe when <div>
// exits. Only release builds update; a dev build (0.0.0-dev) never does.
sealed class Updates : IDisposable
{
    public static readonly string AppVersion =
        (typeof(Updates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev").Split('+')[0];
    const string LatestReleaseUrl = "https://api.github.com/repos/runamonk/div/releases/latest";
    const string UpdaterName = "DivUpdater.exe";
    static readonly string UpdateFolder = Path.Combine(Path.GetTempPath(), "Div-update");

    readonly Func<DivSettings> _settings;
    readonly Action<string?> _status;
    readonly Action _exit;
    readonly DispatcherTimer _timer;
    string? _zip;
    // Turned down this run, so the daily check doesn't keep asking.
    Version? _declined;
    bool _checking;

    // status: a few words for the tray tooltip while downloading, null when done. exit: closes <div>.
    public Updates(Func<DivSettings> settings, Action<string?> status, Action exit)
    {
        _settings = settings;
        _status = status;
        _exit = exit;

        // Left over from the last update. The updater may still be exiting, so this is best effort.
        try
        {
            if (Directory.Exists(UpdateFolder)) Directory.Delete(UpdateFolder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        // <div> runs for days at a time, so look shortly after it starts and then once a day.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromDays(1);
            await CheckQuietly();
        };
        _timer.Start();
    }

    // Only speaks up when there's an update.
    async Task CheckQuietly()
    {
        if (!_settings().CheckForUpdates || _zip is not null || !Version.TryParse(AppVersion, out _)) return;
        try
        {
            await Check(quiet: true);
        }
        catch (Exception ex)
        {
            Log.Write($"Update check failed: {ex.Message}");
        }
    }

    // From the tray menu: says what it found either way.
    public async Task CheckNow()
    {
        try
        {
            await Check(quiet: false);
        }
        catch (Exception ex)
        {
            _status(null);
            await Tell("Couldn't update <div>", ex.Message);
        }
    }

    async Task Check(bool quiet)
    {
        if (_zip is not null)
        {
            await RestartToInstall();
            return;
        }
        if (!Version.TryParse(AppVersion, out var current))
        {
            await Tell("No updates for this build", $"This is a development build ({AppVersion}), so it doesn't update itself.");
            return;
        }
        if (_checking) return;
        _checking = true;
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Div/" + AppVersion);
            using var check = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var release = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseUrl, check.Token));
            var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var latest))
                throw new IOException($"The latest release has an unexpected version ({tag}).");
            if (latest <= current)
            {
                if (!quiet) await Tell("<div> is up to date", $"You're on the latest version ({AppVersion}).");
                return;
            }
            if (quiet && latest == _declined) return;

            var asset = release.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true);
            if (asset.ValueKind == JsonValueKind.Undefined) throw new IOException($"Version {latest} has no Windows download.");
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal))
                throw new IOException($"Version {latest} has no checksum, so it can't be verified.");

            if (!await ConfirmDialog.Show(null, $"<div> {latest} is available", $"You have {AppVersion}.", "Download"))
            {
                _declined = latest;
                return;
            }
            var updater = Path.Combine(AppContext.BaseDirectory, UpdaterName);
            if (!File.Exists(updater))
                throw new IOException($"{UpdaterName} is missing, so this copy can't update itself. Download version {latest} from GitHub instead.");
            Directory.CreateDirectory(UpdateFolder);
            var zip = Path.Combine(UpdateFolder, $"Div-{latest}.zip");
            await Download(http, asset.GetProperty("browser_download_url").GetString()!, zip, digest["sha256:".Length..], latest);
            // The updater runs from temp so nothing in the app folder is in use while it installs.
            File.Copy(updater, Path.Combine(UpdateFolder, UpdaterName), true);
            _zip = zip;
            _status(null);
            Log.Write($"Downloaded <div> {latest}; it installs when <div> exits.");
            await RestartToInstall();
        }
        catch (OperationCanceledException)
        {
            throw new IOException("GitHub didn't answer in time. Try again later.");
        }
        finally
        {
            _checking = false;
        }
    }

    async Task Download(HttpClient http, string url, string zip, string sha256, Version version)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(zip))
            {
                var buffer = new byte[81920];
                long received = 0;
                int shown = -1, read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    int percent = total > 0 ? (int)(received * 100 / total) : 0;
                    if (percent == shown) continue;
                    shown = percent;
                    _status($"downloading {version}… {percent}%");
                }
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The download didn't match its checksum, so it was thrown away. Try again later.");
        }
        catch
        {
            try
            {
                File.Delete(zip);
            }
            catch (IOException)
            {
            }
            throw;
        }
    }

    async Task RestartToInstall()
    {
        if (await ConfirmDialog.Show(null, "Restart <div> to update?", "The update is ready. Choose Cancel to install it the next time <div> exits.", "Restart"))
            _exit();
    }

    // Called as <div> exits.
    public void StartInstaller()
    {
        if (_zip is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(UpdateFolder, UpdaterName))
            {
                ArgumentList = { "--pid", Environment.ProcessId.ToString(), "--zip", _zip,
                    "--app", Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "--from", AppVersion },
            });
        }
        catch (Exception ex)
        {
            Log.Write($"Couldn't start the updater: {ex.Message}");
        }
    }

    static Task<bool> Tell(string title, string message) => ConfirmDialog.Show(null, title, message, "OK", cancelText: null);

    public void Dispose() => _timer.Stop();
}
