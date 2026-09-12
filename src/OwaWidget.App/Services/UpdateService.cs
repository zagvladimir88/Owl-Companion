using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace OwaWidget.App.Services;

public sealed class UpdateService
{
    public const string Repository = "https://github.com/zagvladimir88/Owl-ompanion";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(Repository, null, false));
    }

    public event Action<string>? UpdateReady;

    public static string DisplayVersion
    {
        get
        {
            var informational = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return (informational ?? "1.0.0").Split('+')[0];
        }
    }

    public bool HasPendingUpdate => _pending is not null;

    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled)
        {
            Log.Info("update check skipped: running outside an installed copy");
            return;
        }

        if (_pending is not null)
        {
            return;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync();

            if (update is null)
            {
                Log.Info($"no updates, current {_manager.CurrentVersion}");
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            Log.Info($"downloading update {version}");

            await _manager.DownloadUpdatesAsync(update);

            _pending = update;
            Log.Info($"update {version} ready to apply");
            UpdateReady?.Invoke(version);
        }
        catch (Exception exception)
        {
            Log.Error("update check failed", exception);
        }
    }

    public void ApplyAndRestart()
    {
        if (_pending is null)
        {
            return;
        }

        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    public void ApplyOnExit()
    {
        if (_pending is null)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception exception)
        {
            Log.Error("scheduling update on exit failed", exception);
        }
    }
}