using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxSyncPlugin
{
    public class CustomFieldConfig
    {
        public string? name { get; set; }
        public string? value { get; set; }
    }

    public class MetadataEntry
    {
        public string? game_id { get; set; }
        public string? game_source { get; set; }
        public string? metadata_dir { get; set; }
        public string? metadata_source { get; set; }
        public string? metadata_id { get; set; }
        public string? title { get; set; }
        public string? release_date { get; set; }
        public string? release_type { get; set; }
        public string? description { get; set; }
        public JsonElement developer { get; set; }
        public JsonElement publisher { get; set; }
        public string? region { get; set; }
        public JsonElement genre { get; set; }
        public JsonElement play_mode { get; set; }
        public int? max_players { get; set; }
        public string? rating { get; set; }
        public JsonElement series { get; set; }
        public string? video_url { get; set; }
        public string? wikipedia_url { get; set; }
        public Dictionary<string, string>? field_sources { get; set; }
        public string? added_at { get; set; }
        public string? updated_at { get; set; }
    }

    public class MediaEntry
    {
        public string? game_id { get; set; }
        public string? game_source { get; set; }
        public string? media_dir { get; set; }
        public string? media_source { get; set; }
        public string? media_id { get; set; }
        public string? manual_path { get; set; }
        public string? music_path { get; set; }
        public Dictionary<string, string>? image_paths { get; set; }
        public Dictionary<string, string>? video_paths { get; set; }
        public Dictionary<string, JsonElement>? asset_sources { get; set; }
        public string? added_at { get; set; }
        public string? updated_at { get; set; }
    }

    public class ManifestEntry
    {
        public string? global_id { get; set; }
        public string? game_id { get; set; }
        public string? game_source { get; set; }
        public string? game_name { get; set; }
        public string? game_folder { get; set; }
        public string? game_dir { get; set; }
        public string? game_version { get; set; }
        public string? application_path { get; set; }
        public string? application_arguments { get; set; }
        public string? config_app_path { get; set; }
        public string? config_app_arguments { get; set; }
        public bool? is_installed { get; set; }
        public bool? is_favorite { get; set; }
        public bool? is_broken { get; set; }
        public bool? is_portable { get; set; }
        public bool? is_hidden { get; set; }
        public bool? use_dosbox { get; set; }
        public string? dosbox_config_path { get; set; }
        public string? dosbox_version_path { get; set; }
        public string? status { get; set; }
        public string? progress { get; set; }
        public string? platform { get; set; }
        public string? source { get; set; }
        public string? metadata_source { get; set; }
        public string? metadata_id { get; set; }
        public string? media_source { get; set; }
        public string? media_id { get; set; }
        public MetadataEntry? metadata { get; set; }
        public MediaEntry? media { get; set; }
        public List<CustomFieldConfig>? custom_fields { get; set; }
        public List<AddlAppConfig>? addl_apps { get; set; }
        public Dictionary<string, JsonElement>? extras { get; set; }
        public string? added_at { get; set; }
        public string? updated_at { get; set; }
    }

    public class AddlAppConfig
    {
        public string? app_name { get; set; }
        public string? app_path { get; set; }
        public string? app_arguments { get; set; }
        public bool? auto_run_after { get; set; }
        public bool? auto_run_before { get; set; }
        public bool? wait_for_exit { get; set; }
        public int? priority { get; set; }
    }

    public static class SyncEngine
    {
        private sealed record SelectedGameSnapshot(
            IGame Game,
            string? GameId,
            string? Title,
            string? Source,
            string? CustomGameId,
            string? CustomGameSource);

        private sealed record PendingSourceMatch(
            string GameId,
            ManifestEntry Entry,
            IGame? MatchedGame,
            string? ExistingGameId);

        private sealed record PendingSelectedMatch(
            IGame Game,
            string ResolvedGameId,
            ManifestEntry Entry,
            SourceConfig SourceConfig,
            string? GameId);

        private static readonly Dictionary<string, string> SourceDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "amazon", "Amazon Games" },
            { "depot", "Depot" },
            { "epic", "Epic Games" },
            { "gog", "GOG.com" },
            { "local", "Local" },
            { "steam", "Steam" },
            { "xbox", "Xbox" },
            { "exodos", "eXoDOS" },
            { "exowin3x", "eXoWin3x" },
            { "exowin9x", "eXoWin9x" },
            { "exoappleiigs", "eXoAppleIIGS" },
            { "exoscummvm", "eXoScummVM" },
            { "exodreamm", "eXoDREAMM" }
        };

        internal static void InvokeOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
                return;
            }
            action();
        }

        private static T RunOnUiThread<T>(Func<T> func, Action<Action> uiDispatcher)
        {
            T result = default!;
            uiDispatcher(() => result = func());
            return result;
        }

        internal static string ResolveSourceName(ManifestEntry entry, SourceConfig? sourceConfig = null)
        {
            if (!string.IsNullOrWhiteSpace(entry.source))
                return entry.source.Trim();
            if (!string.IsNullOrWhiteSpace(sourceConfig?.SourceName))
                return sourceConfig.SourceName.Trim();
            if (!string.IsNullOrWhiteSpace(entry.game_source))
            {
                var key = entry.game_source.Trim();
                return SourceDisplayNames.TryGetValue(key, out var display) ? display : key;
            }
            return string.Empty;
        }

        public static void SyncAll(List<SourceConfig> sources, IProgress<string>? progress = null, CancellationToken token = default)
        {
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                SyncSource(source, true, true, progress, token);
            }
        }

        public static void SyncSource(SourceConfig source, bool syncGames = true, bool syncMedia = true, IProgress<string>? progress = null, CancellationToken token = default)
        {
            SyncSource(
                source,
                syncGames,
                syncMedia,
                progress,
                token,
                PluginHelper.DataManager,
                () => PluginHelper.LaunchBoxMainViewModel?.RefreshData(),
                InvokeOnUiThread,
                null);
        }

        internal static void SyncSource(
            SourceConfig source,
            bool syncGames,
            bool syncMedia,
            IProgress<string>? progress,
            CancellationToken token,
            IDataManager dataManager,
            Action? refreshData,
            Action<Action> uiDispatcher,
            string? mediaRootOverride = null)
        {
            if (string.IsNullOrEmpty(source.ManifestPath) || !File.Exists(source.ManifestPath)) return;

            token.ThrowIfCancellationRequested();
            var manifest = LoadManifest(source.ManifestPath);
            if (manifest.Count == 0) return;

            var uniqueSources = manifest.Values
                .Select(v => ResolveSourceName(v, source))
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var pendingMatches = RunOnUiThread(() =>
            {
                token.ThrowIfCancellationRequested();
                var allGames = (dataManager.GetAllGames() ?? Array.Empty<IGame>()).ToList();
                var sourceGames = uniqueSources.Count > 0
                    ? allGames.Where(g => !string.IsNullOrEmpty(g.Source) && uniqueSources.Contains(g.Source)).ToList()
                    : allGames;

                var list = new List<PendingSourceMatch>(manifest.Count);
                foreach (var manifestKvp in manifest)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = manifestKvp.Value;
                    var gameId = !string.IsNullOrWhiteSpace(entry.game_id) ? entry.game_id! : manifestKvp.Key;
                    var matchedGame = MatchGame(gameId, entry, allGames, sourceGames);
                    list.Add(new PendingSourceMatch(gameId, entry, matchedGame, matchedGame?.Id));
                }
                return list;
            }, uiDispatcher);

            int total = pendingMatches.Count;
            int count = 0;

            if (syncMedia)
            {
                foreach (var item in pendingMatches)
                {
                    token.ThrowIfCancellationRequested();
                    count++;
                    if (count % 10 == 0 || count == total || count == 1)
                    {
                        progress?.Report($"Scanning {item.Entry.game_name} ({count}/{total})...");
                    }
                    if (!string.IsNullOrEmpty(item.ExistingGameId))
                    {
                        CopyGameMedia(item.ExistingGameId, item.Entry, mediaRootOverride);
                    }
                }
            }
            else
            {
                progress?.Report($"Updating {total} games...");
            }

            if (syncGames)
            {
                token.ThrowIfCancellationRequested();

                var newGameMediaTargets = RunOnUiThread(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var addedTargets = new List<(string GameId, ManifestEntry Entry)>();

                    foreach (var item in pendingMatches)
                    {
                        var targetGame = item.MatchedGame;
                        bool isNew = false;

                        if (targetGame == null)
                        {
                            targetGame = dataManager.AddNewGame(item.Entry.game_name ?? string.Empty);
                            isNew = true;
                        }

                        if (targetGame != null)
                        {
                            UpdateGameData(targetGame, item.GameId, item.Entry, source);
                            if (isNew && !string.IsNullOrEmpty(targetGame.Id))
                            {
                                addedTargets.Add((targetGame.Id, item.Entry));
                            }
                        }
                    }

                    dataManager.Save();
                    return addedTargets;
                }, uiDispatcher);

                if (syncMedia && newGameMediaTargets.Count > 0)
                {
                    foreach (var (newGameId, entry) in newGameMediaTargets)
                    {
                        token.ThrowIfCancellationRequested();
                        CopyGameMedia(newGameId, entry, mediaRootOverride);
                    }
                }
            }

            if (syncGames || syncMedia)
            {
                uiDispatcher(() => refreshData?.Invoke());
            }
        }

        public static void SyncSelectedGames(IGame[] selectedGames, List<SourceConfig> sources, bool syncGames = true, bool syncMedia = true, IProgress<string>? progress = null, CancellationToken token = default)
        {
            SyncSelectedGames(
                selectedGames,
                sources,
                syncGames,
                syncMedia,
                progress,
                token,
                PluginHelper.DataManager,
                () => PluginHelper.LaunchBoxMainViewModel?.RefreshData(),
                InvokeOnUiThread,
                null);
        }

        internal static void SyncSelectedGames(
            IGame[] selectedGames,
            List<SourceConfig> sources,
            bool syncGames,
            bool syncMedia,
            IProgress<string>? progress,
            CancellationToken token,
            IDataManager dataManager,
            Action? refreshData,
            Action<Action> uiDispatcher,
            string? mediaRootOverride = null)
        {
            if (selectedGames == null || selectedGames.Length == 0) return;

            var manifests = new List<(SourceConfig Source, Dictionary<string, ManifestEntry> Entries)>();
            foreach (var s in sources)
            {
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(s.ManifestPath) && File.Exists(s.ManifestPath))
                {
                    manifests.Add((s, LoadManifest(s.ManifestPath)));
                }
            }

            if (manifests.Count == 0) return;

            var snapshots = RunOnUiThread(() =>
            {
                token.ThrowIfCancellationRequested();
                var list = new List<SelectedGameSnapshot>(selectedGames.Length);
                foreach (var game in selectedGames)
                {
                    var customFields = game.GetAllCustomFields() ?? Array.Empty<ICustomField>();
                    var gameIdField = customFields.FirstOrDefault(cf => string.Equals(cf.Name, "Game ID", StringComparison.OrdinalIgnoreCase));
                    var gameSourceField = customFields.FirstOrDefault(cf => string.Equals(cf.Name, "Game Source", StringComparison.OrdinalIgnoreCase));
                    list.Add(new SelectedGameSnapshot(
                        game,
                        game.Id,
                        game.Title,
                        game.Source,
                        gameIdField?.Value,
                        gameSourceField?.Value));
                }
                return list;
            }, uiDispatcher);

            var pendingMatches = new List<PendingSelectedMatch>();
            int total = snapshots.Count;
            int count = 0;

            foreach (var snap in snapshots)
            {
                token.ThrowIfCancellationRequested();
                count++;
                if (count % 10 == 0 || count == total || count == 1)
                {
                    progress?.Report($"Scanning {snap.Title} ({count}/{total})...");
                }

                if (!TryFindManifestMatch(snap, manifests, out var matchedSource, out var match) || matchedSource == null)
                {
                    continue;
                }

                var entry = match.Value;
                var resolvedGameId = !string.IsNullOrWhiteSpace(entry.game_id) ? entry.game_id! : match.Key;

                if (syncMedia && !string.IsNullOrEmpty(snap.GameId))
                {
                    CopyGameMedia(snap.GameId, entry, mediaRootOverride);
                }

                pendingMatches.Add(new PendingSelectedMatch(snap.Game, resolvedGameId, entry, matchedSource, snap.GameId));
            }

            if (pendingMatches.Count == 0) return;

            if (syncGames)
            {
                token.ThrowIfCancellationRequested();
                uiDispatcher(() =>
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var item in pendingMatches)
                    {
                        UpdateGameData(item.Game, item.ResolvedGameId, item.Entry, item.SourceConfig);
                    }
                    dataManager.Save();
                    refreshData?.Invoke();
                });
            }
            else if (syncMedia)
            {
                uiDispatcher(() => refreshData?.Invoke());
            }
        }

        private static bool TryFindManifestMatch(
            SelectedGameSnapshot snap,
            List<(SourceConfig Source, Dictionary<string, ManifestEntry> Entries)> manifests,
            out SourceConfig? matchedSource,
            out KeyValuePair<string, ManifestEntry> match)
        {
            matchedSource = null;
            match = default;

            foreach (var (sourceCfg, manifest) in manifests)
            {
                if (!string.IsNullOrEmpty(snap.GameId))
                {
                    var byGlobalId = manifest.FirstOrDefault(m =>
                        !string.IsNullOrEmpty(m.Value.global_id) &&
                        string.Equals(m.Value.global_id, snap.GameId, StringComparison.OrdinalIgnoreCase));
                    if (byGlobalId.Key != null)
                    {
                        matchedSource = sourceCfg;
                        match = byGlobalId;
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(snap.CustomGameId))
                {
                    var byGameId = manifest.FirstOrDefault(m =>
                    {
                        var entryId = !string.IsNullOrWhiteSpace(m.Value.game_id) ? m.Value.game_id : m.Key;
                        if (!string.Equals(entryId, snap.CustomGameId, StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (!string.IsNullOrEmpty(snap.CustomGameSource) && !string.IsNullOrEmpty(m.Value.game_source))
                            return string.Equals(m.Value.game_source, snap.CustomGameSource, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(snap.Source))
                        {
                            var resolved = ResolveSourceName(m.Value, sourceCfg);
                            if (!string.IsNullOrEmpty(resolved))
                                return string.Equals(resolved, snap.Source, StringComparison.OrdinalIgnoreCase);
                        }
                        return true;
                    });
                    if (byGameId.Key != null)
                    {
                        matchedSource = sourceCfg;
                        match = byGameId;
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(snap.Title) && !string.IsNullOrEmpty(snap.Source))
                {
                    var titleMatch = manifest.FirstOrDefault(m =>
                        string.Equals(ResolveSourceName(m.Value, sourceCfg), snap.Source, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(m.Value.game_name, snap.Title, StringComparison.OrdinalIgnoreCase));
                    if (titleMatch.Key != null)
                    {
                        matchedSource = sourceCfg;
                        match = titleMatch;
                        return true;
                    }
                }
            }

            return false;
        }

        private static void UpsertCustomField(IGame game, string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            var customFields = game.GetAllCustomFields() ?? Array.Empty<ICustomField>();
            var matchedField = customFields.FirstOrDefault(cf => string.Equals(cf.Name, name, StringComparison.OrdinalIgnoreCase));
            if (matchedField == null)
            {
                matchedField = game.AddNewCustomField();
                matchedField.Name = name;
            }
            matchedField.Value = value ?? string.Empty;
        }

        private static bool HasJsonValue(JsonElement element)
        {
            return element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined;
        }

        internal static bool TryParseReleaseDate(string? value, out DateTime releaseDate)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                releaseDate = default;
                return false;
            }
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out releaseDate);
        }

        internal static void UpdateGameData(IGame game, string gameId, ManifestEntry entry, SourceConfig? sourceConfig = null)
        {
            if (!string.IsNullOrEmpty(entry.game_name))
            {
                game.Title = entry.game_name;
            }

            var resolvedSource = ResolveSourceName(entry, sourceConfig);
            if (!string.IsNullOrEmpty(resolvedSource))
            {
                game.Source = resolvedSource;
            }
            if (!string.IsNullOrEmpty(entry.platform))
            {
                game.Platform = entry.platform;
            }
            if (entry.game_dir != null)
            {
                game.RootFolder = entry.game_dir;
            }
            if (entry.is_installed.HasValue)
            {
                game.Installed = entry.is_installed.Value;
            }
            if (entry.game_version != null)
            {
                game.Version = entry.game_version;
            }

            bool resolveRelative = entry.is_installed ?? (game.Installed ?? false);
            var effectiveGameDir = entry.game_dir ?? game.RootFolder;

            if (entry.application_path != null)
            {
                game.ApplicationPath = ResolvePath(entry.application_path, resolveRelative, effectiveGameDir);
            }
            if (entry.application_arguments != null)
            {
                game.CommandLine = entry.application_arguments;
            }
            if (entry.config_app_path != null)
            {
                game.ConfigurationPath = ResolvePath(entry.config_app_path, resolveRelative, effectiveGameDir);
            }
            if (entry.config_app_arguments != null)
            {
                game.ConfigurationCommandLine = entry.config_app_arguments;
            }

            if (entry.is_favorite.HasValue)
            {
                game.Favorite = entry.is_favorite.Value;
            }
            if (entry.is_broken.HasValue)
            {
                game.Broken = entry.is_broken.Value;
            }
            if (entry.is_portable.HasValue)
            {
                game.Portable = entry.is_portable.Value;
            }
            if (entry.is_hidden.HasValue)
            {
                game.Hide = entry.is_hidden.Value;
            }
            if (entry.use_dosbox.HasValue)
            {
                game.UseDosBox = entry.use_dosbox.Value;
            }
            if (entry.dosbox_config_path != null)
            {
                game.DosBoxConfigurationPath = ResolvePath(entry.dosbox_config_path, resolveRelative, effectiveGameDir);
            }
            if (entry.status != null)
            {
                game.Status = entry.status;
            }
            if (entry.progress != null)
            {
                game.Progress = entry.progress;
            }

            if (entry.metadata != null)
            {
                if (entry.metadata.description != null)
                {
                    game.Notes = entry.metadata.description;
                }
                if (HasJsonValue(entry.metadata.developer))
                {
                    game.Developer = FormatMetadataField(entry.metadata.developer, splitCommas: false);
                }
                if (HasJsonValue(entry.metadata.publisher))
                {
                    game.Publisher = FormatMetadataField(entry.metadata.publisher, splitCommas: false);
                }
                if (HasJsonValue(entry.metadata.genre))
                {
                    game.GenresString = FormatMetadataField(entry.metadata.genre, splitCommas: true);
                }
                if (HasJsonValue(entry.metadata.play_mode))
                {
                    game.PlayMode = FormatMetadataField(entry.metadata.play_mode, splitCommas: true);
                }
                if (entry.metadata.region != null)
                {
                    game.Region = entry.metadata.region;
                }
                if (entry.metadata.release_type != null)
                {
                    game.ReleaseType = entry.metadata.release_type;
                }
                if (entry.metadata.max_players.HasValue)
                {
                    game.MaxPlayers = entry.metadata.max_players.Value;
                }
                if (TryParseReleaseDate(entry.metadata.release_date, out var releaseDate))
                {
                    game.ReleaseDate = releaseDate;
                }
                if (entry.metadata.rating != null)
                {
                    game.Rating = entry.metadata.rating;
                }
                if (HasJsonValue(entry.metadata.series))
                {
                    game.Series = FormatMetadataField(entry.metadata.series, splitCommas: false);
                }
                if (entry.metadata.video_url != null)
                {
                    game.VideoUrl = entry.metadata.video_url;
                }
                if (entry.metadata.wikipedia_url != null)
                {
                    game.WikipediaUrl = entry.metadata.wikipedia_url;
                }
            }

            if (entry.media != null)
            {
                if (entry.media.manual_path != null)
                {
                    game.ManualPath = string.IsNullOrEmpty(entry.media.manual_path)
                        ? string.Empty
                        : ResolvePath(entry.media.manual_path, true, entry.media.media_dir);
                }
                if (entry.media.music_path != null)
                {
                    game.MusicPath = string.IsNullOrEmpty(entry.media.music_path)
                        ? string.Empty
                        : ResolvePath(entry.media.music_path, true, entry.media.media_dir);
                }
            }

            if (!string.IsNullOrEmpty(gameId))
            {
                UpsertCustomField(game, "Game ID", gameId);
            }
            if (!string.IsNullOrWhiteSpace(entry.game_source))
            {
                UpsertCustomField(game, "Game Source", entry.game_source.Trim());
            }
            if (!string.IsNullOrWhiteSpace(entry.metadata_source))
            {
                UpsertCustomField(game, "Metadata Source", entry.metadata_source.Trim());
            }
            if (!string.IsNullOrWhiteSpace(entry.metadata_id))
            {
                UpsertCustomField(game, "Metadata ID", entry.metadata_id.Trim());
            }
            if (!string.IsNullOrWhiteSpace(entry.media_source))
            {
                UpsertCustomField(game, "Media Source", entry.media_source.Trim());
            }
            if (!string.IsNullOrWhiteSpace(entry.media_id))
            {
                UpsertCustomField(game, "Media ID", entry.media_id.Trim());
            }

            if (entry.custom_fields != null)
            {
                foreach (var field in entry.custom_fields)
                {
                    if (!string.IsNullOrEmpty(field.name))
                    {
                        UpsertCustomField(game, field.name, field.value ?? string.Empty);
                    }
                }
            }

            if (entry.addl_apps != null)
            {
                foreach (var appConfig in entry.addl_apps)
                {
                    var appName = appConfig.app_name ?? string.Empty;
                    if (string.IsNullOrEmpty(appName)) continue;

                    var existingApps = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>();
                    var matchedApp = existingApps.FirstOrDefault(a => string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase));

                    if (matchedApp == null)
                    {
                        matchedApp = game.AddNewAdditionalApplication();
                    }

                    matchedApp.Name = appName;
                    if (appConfig.app_path != null)
                    {
                        matchedApp.ApplicationPath = appConfig.app_path;
                    }
                    if (appConfig.app_arguments != null)
                    {
                        matchedApp.CommandLine = appConfig.app_arguments;
                    }
                    if (appConfig.auto_run_after.HasValue)
                    {
                        matchedApp.AutoRunAfter = appConfig.auto_run_after.Value;
                    }
                    if (appConfig.auto_run_before.HasValue)
                    {
                        matchedApp.AutoRunBefore = appConfig.auto_run_before.Value;
                    }
                    if (appConfig.wait_for_exit.HasValue)
                    {
                        matchedApp.WaitForExit = appConfig.wait_for_exit.Value;
                    }
                    if (appConfig.priority.HasValue)
                    {
                        matchedApp.Priority = appConfig.priority.Value;
                    }
                }
            }
        }

        internal static string ResolvePath(string? path, bool resolveRelative, string? baseDir)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (Path.IsPathRooted(path) || path.Contains("://")) return path;
            if (resolveRelative && !string.IsNullOrEmpty(baseDir))
            {
                var rel = path.StartsWith("./") || path.StartsWith(".\\") ? path.Substring(2) : path;
                return Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));
            }
            return path;
        }

        internal static string FormatMetadataField(JsonElement element, bool splitCommas = true)
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                return string.Empty;

            if (element.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var val = item.GetString();
                        if (!string.IsNullOrEmpty(val)) list.Add(val);
                    }
                }
                return string.Join("; ", list);
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var val = element.GetString() ?? string.Empty;
                return splitCommas && val.Contains(",")
                    ? val.Replace(", ", "; ").Replace(",", "; ")
                    : val;
            }

            return element.ToString();
        }

        private static IGame? MatchGame(string gameId, ManifestEntry entry, List<IGame> allGames, List<IGame> sourceGames)
        {
            // 1. Match by global_id (LaunchBox internal Game.Id)
            if (!string.IsNullOrEmpty(entry.global_id))
            {
                var byGlobalId = allGames.FirstOrDefault(g =>
                    string.Equals(g.Id, entry.global_id, StringComparison.OrdinalIgnoreCase));
                if (byGlobalId != null) return byGlobalId;
            }

            // 2. Match by Game ID custom field within source games
            if (!string.IsNullOrEmpty(gameId))
            {
                var byGameId = sourceGames.FirstOrDefault(g =>
                {
                    var fields = g.GetAllCustomFields() ?? Array.Empty<ICustomField>();
                    return fields.Any(cf =>
                        string.Equals(cf.Name, "Game ID", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(cf.Value, gameId, StringComparison.OrdinalIgnoreCase));
                });
                if (byGameId != null) return byGameId;
            }

            // 3. Match by game directory folder name
            var folderName = !string.IsNullOrEmpty(entry.game_dir)
                ? Path.GetFileName(entry.game_dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : string.Empty;
            if (!string.IsNullOrEmpty(folderName))
            {
                var byFolder = sourceGames.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.RootFolder) &&
                    string.Equals(
                        Path.GetFileName(g.RootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        folderName,
                        StringComparison.OrdinalIgnoreCase));
                if (byFolder != null) return byFolder;
            }

            // 4. Match by title within source games
            if (!string.IsNullOrEmpty(entry.game_name))
            {
                var byTitle = sourceGames.FirstOrDefault(g =>
                    string.Equals(g.Title, entry.game_name, StringComparison.OrdinalIgnoreCase));
                if (byTitle != null) return byTitle;
            }

            return null;
        }

        internal static Dictionary<string, ManifestEntry> LoadManifest(string path)
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(json, options) ?? new Dictionary<string, ManifestEntry>();
        }

        internal static string GetLaunchBoxMediaDir(string mediaType, string? mediaRootOverride = null)
        {
            if (!string.IsNullOrEmpty(mediaRootOverride))
            {
                return Path.Combine(mediaRootOverride, mediaType);
            }

            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var pluginDir = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var current = new DirectoryInfo(pluginDir);
            for (int i = 0; i < 4 && current != null; i++)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Data")))
                {
                    return Path.Combine(current.FullName, mediaType);
                }
                current = current.Parent;
            }

            var pluginsDir = Path.GetDirectoryName(pluginDir);
            var lbRoot = Path.GetDirectoryName(pluginsDir) ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(lbRoot, mediaType);
        }

        private static string? ResolveMediaFile(string? mediaDir, string? metadataDir, string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return null;
            if (!string.IsNullOrEmpty(mediaDir))
            {
                var candidate = ResolvePath(relPath, true, mediaDir);
                if (File.Exists(candidate)) return candidate;
            }
            if (!string.IsNullOrEmpty(metadataDir))
            {
                var candidate = ResolvePath(relPath, true, metadataDir);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        internal static void CopyGameMedia(string gameId, ManifestEntry entry, string? mediaRootOverride = null)
        {
            if (entry.media == null || string.IsNullOrEmpty(gameId)) return;

            var mediaDir = entry.media.media_dir;
            var metadataDir = entry.metadata?.metadata_dir;
            if (string.IsNullOrEmpty(mediaDir) && string.IsNullOrEmpty(metadataDir)) return;

            var platform = string.IsNullOrEmpty(entry.platform) ? "Windows" : entry.platform;

            if (entry.media.image_paths != null && entry.media.image_paths.Count > 0)
            {
                var imagesDir = GetLaunchBoxMediaDir("Images", mediaRootOverride);
                var folderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "background", "Fanart" },
                    { "cover", "Poster" },
                    { "hero", "Banner" },
                    { "logo", "Clear Logo" },
                    { "icon", "Icon" },
                    { "screenshot", "Screenshot" },
                    { "box_back", "Back" },
                    { "box_3d", "Box - 3D" },
                    { "box_full", "Box - Full" },
                    { "box_spine", "Box - Spine" },
                    { "cart_front", "Cart - Front" },
                    { "cart_back", "Cart - Back" },
                    { "cart_3d", "Cart - 3D" },
                    { "disc", "Disc" },
                    { "screenshot_game_title", "Screenshot - Game Title" },
                    { "screenshot_game_over", "Screenshot - Game Over" },
                    { "screenshot_game_select", "Screenshot - Game Select" },
                    { "screenshot_high_scores", "Screenshot - High Scores" },
                    { "advertisement_flyer_front", "Advertisement Flyer - Front" },
                    { "advertisement_flyer_back", "Advertisement Flyer - Back" }
                };

                foreach (var kvp in entry.media.image_paths)
                {
                    var role = kvp.Key;
                    var relPath = kvp.Value;

                    var (baseRole, suffix) = ParseRole(role);
                    if (string.IsNullOrEmpty(baseRole) || string.IsNullOrEmpty(relPath) || !folderMap.TryGetValue(baseRole, out var lbSubFolder))
                    {
                        continue;
                    }

                    var sourcePath = ResolveMediaFile(mediaDir, metadataDir, relPath);
                    if (sourcePath == null) continue;

                    var destDir = Path.Combine(imagesDir, platform, lbSubFolder);
                    var ext = Path.GetExtension(sourcePath);
                    var destFileName = gameId + suffix + ext;
                    var destPath = Path.Combine(destDir, destFileName);

                    try
                    {
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        File.Copy(sourcePath, destPath, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to copy image {sourcePath} to {destPath}: {ex.Message}");
                    }
                }
            }

            if (entry.media.video_paths != null && entry.media.video_paths.Count > 0)
            {
                var videosDir = GetLaunchBoxMediaDir("Videos", mediaRootOverride);
                var videoFolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "marquee", "Marquee" },
                    { "recordings", "Recordings" },
                    { "theme", "Theme" },
                    { "trailer", "Trailer" }
                };

                foreach (var kvp in entry.media.video_paths)
                {
                    var role = kvp.Key;
                    var relPath = kvp.Value;

                    var (baseRole, suffix) = ParseRole(role);
                    if (string.IsNullOrEmpty(baseRole) || string.IsNullOrEmpty(relPath))
                    {
                        continue;
                    }

                    if (!videoFolderMap.TryGetValue(baseRole, out var lbSubFolder))
                    {
                        lbSubFolder = char.ToUpperInvariant(baseRole[0]) + baseRole.Substring(1);
                    }

                    var sourcePath = ResolveMediaFile(mediaDir, metadataDir, relPath);
                    if (sourcePath == null) continue;

                    var destDir = Path.Combine(videosDir, platform, lbSubFolder);
                    var ext = Path.GetExtension(sourcePath);
                    var destFileName = gameId + suffix + ext;
                    var destPath = Path.Combine(destDir, destFileName);

                    try
                    {
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        File.Copy(sourcePath, destPath, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to copy video {sourcePath} to {destPath}: {ex.Message}");
                    }
                }
            }
        }

        internal static (string baseRole, string suffix) ParseRole(string? role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return (string.Empty, string.Empty);
            }

            var baseRole = role;
            var suffix = "";
            var dashIndex = role.LastIndexOf('-');
            if (dashIndex > 0 && dashIndex < role.Length - 1)
            {
                var suffixPart = role.Substring(dashIndex + 1);
                if (int.TryParse(suffixPart, out _))
                {
                    baseRole = role.Substring(0, dashIndex);
                    suffix = "-" + suffixPart;
                }
            }
            return (baseRole, suffix);
        }
    }
}
