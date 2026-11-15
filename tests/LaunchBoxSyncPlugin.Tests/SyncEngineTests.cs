using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchBoxSyncPlugin.Tests
{
    public sealed class SyncEngineTests : IDisposable
    {
        private readonly string _tempDir;

        public SyncEngineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LaunchBoxSyncPluginTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void LoadManifest_ParsesV2FieldsIncludingSeriesArrayAndFieldSources()
        {
            var manifestPath = Path.Combine(_tempDir, "v2_manifest.json");
            File.WriteAllText(
                manifestPath,
                """
                {
                  "g1": {
                    "game_id": "g1",
                    "game_name": "Example Game",
                    "is_installed": true,
                    "is_favorite": true,
                    "is_broken": true,
                    "is_portable": false,
                    "is_hidden": true,
                    "use_dosbox": true,
                    "dosbox_config_path": "dosbox.conf",
                    "status": "Playing",
                    "progress": "50%",
                    "metadata": {
                      "developer": "Acme, Inc.",
                      "publisher": ["Pub One, LLC", "Pub Two"],
                      "genre": "Action, Adventure",
                      "series": ["Trails", "Legend"],
                      "release_date": "2026-07-06T00:00:00",
                      "field_sources": {
                        "title": "igdb",
                        "developer": "igdb"
                      }
                    }
                  }
                }
                """);

            var manifest = SyncEngine.LoadManifest(manifestPath);
            Assert.True(manifest.TryGetValue("g1", out var entry));
            Assert.NotNull(entry);
            Assert.True(entry!.is_favorite);
            Assert.True(entry.is_broken);
            Assert.False(entry.is_portable);
            Assert.True(entry.is_hidden);
            Assert.True(entry.use_dosbox);
            Assert.Equal("Playing", entry.status);
            Assert.Equal("50%", entry.progress);
            Assert.NotNull(entry.metadata);
            Assert.Equal("Acme, Inc.", SyncEngine.FormatMetadataField(entry.metadata!.developer, splitCommas: false));
            Assert.Equal("Pub One, LLC; Pub Two", SyncEngine.FormatMetadataField(entry.metadata.publisher, splitCommas: false));
            Assert.Equal("Action; Adventure", SyncEngine.FormatMetadataField(entry.metadata.genre, splitCommas: true));
            Assert.Equal("Trails; Legend", SyncEngine.FormatMetadataField(entry.metadata.series, splitCommas: false));
            Assert.Equal("igdb", entry.metadata.field_sources?["developer"]);
        }

        [Fact]
        public void TryParseReleaseDate_UsesInvariantCultureRegardlessOfThreadCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.True(SyncEngine.TryParseReleaseDate("07/25/2024", out var usDate));
                Assert.Equal(new DateTime(2024, 7, 25), usDate.Date);

                Assert.True(SyncEngine.TryParseReleaseDate("2026-07-06T00:00:00", out var isoDate));
                Assert.Equal(new DateTime(2026, 7, 6), isoDate.Date);

                Assert.False(SyncEngine.TryParseReleaseDate("not-a-date", out _));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void ResolveSourceName_FollowsExplicitConfigAndDisplayMapPrecedence()
        {
            var cfg = new SourceConfig { SourceName = "Override Source" };

            Assert.Equal("Explicit", SyncEngine.ResolveSourceName(new ManifestEntry { source = " Explicit ", game_source = "steam" }, cfg));
            Assert.Equal("Override Source", SyncEngine.ResolveSourceName(new ManifestEntry { game_source = "steam" }, cfg));
            Assert.Equal("Epic Games", SyncEngine.ResolveSourceName(new ManifestEntry { game_source = "epic" }, null));
            Assert.Equal("CustomStore", SyncEngine.ResolveSourceName(new ManifestEntry { game_source = "CustomStore" }, null));
        }

        [Fact]
        public void UpdateGameData_PreservesOptionalFieldsWhenOmittedFromManifest()
        {
            var game = LaunchBoxFakes.CreateGame(id: "lb-1", title: "Existing Title");
            game.Favorite = true;
            game.Hide = false;
            game.Broken = false;
            game.Portable = true;
            game.Status = "Completed";
            game.Notes = "Keep these notes";
            game.Rating = "M";
            game.Series = "Keep Series";
            game.ManualPath = @"C:\Manuals\existing.pdf";
            game.MusicPath = @"C:\Music\theme.mp3";
            game.Installed = true;

            var partialEntry = new ManifestEntry
            {
                game_id = "ext-1",
                game_source = "local",
                game_dir = @"C:\Games\Example",
                metadata = new MetadataEntry
                {
                    developer = JsonSerializer.SerializeToElement("Company, Inc."),
                    publisher = JsonSerializer.SerializeToElement("Pub, Ltd."),
                    genre = JsonSerializer.SerializeToElement("RPG, Strategy")
                },
                media = null
            };

            SyncEngine.UpdateGameData(game, "ext-1", partialEntry);

            Assert.Equal("Existing Title", game.Title);
            Assert.True(game.Favorite);
            Assert.False(game.Hide);
            Assert.False(game.Broken);
            Assert.True(game.Portable);
            Assert.Equal("Completed", game.Status);
            Assert.Equal("Keep these notes", game.Notes);
            Assert.Equal("M", game.Rating);
            Assert.Equal("Keep Series", game.Series);
            Assert.Equal(@"C:\Manuals\existing.pdf", game.ManualPath);
            Assert.Equal(@"C:\Music\theme.mp3", game.MusicPath);
            Assert.Equal("Company, Inc.", game.Developer);
            Assert.Equal("Pub, Ltd.", game.Publisher);
            Assert.Equal("RPG; Strategy", game.GenresString);
        }

        [Fact]
        public void CopyGameMedia_HandlesEmptyBaseRoleAndCopiesImagesCleanly()
        {
            var mediaSourceDir = Path.Combine(_tempDir, "source_media");
            var lbRootDir = Path.Combine(_tempDir, "launchbox");
            Directory.CreateDirectory(mediaSourceDir);
            Directory.CreateDirectory(lbRootDir);

            var coverFile = Path.Combine(mediaSourceDir, "cover.jpg");
            var trailerFile = Path.Combine(mediaSourceDir, "trailer.mp4");
            File.WriteAllText(coverFile, "img-bytes");
            File.WriteAllText(trailerFile, "vid-bytes");

            var entry = new ManifestEntry
            {
                platform = "Windows",
                media = new MediaEntry
                {
                    media_dir = mediaSourceDir,
                    image_paths = new Dictionary<string, string>
                    {
                        { "cover-02", "cover.jpg" },
                        { "-02", "cover.jpg" }
                    },
                    video_paths = new Dictionary<string, string>
                    {
                        { "-01", "trailer.mp4" },
                        { "customclip", "trailer.mp4" }
                    }
                }
            };

            SyncEngine.CopyGameMedia("game-123", entry, mediaRootOverride: lbRootDir);

            var expectedImage = Path.Combine(lbRootDir, "Images", "Windows", "Poster", "game-123-02.jpg");
            var expectedCustomVideo = Path.Combine(lbRootDir, "Videos", "Windows", "Customclip", "game-123.mp4");
            Assert.True(File.Exists(expectedImage));
            Assert.True(File.Exists(expectedCustomVideo));
        }

        [Fact]
        public async Task SyncSource_MarshalsDataManagerAccessToUiDispatcher_FromBackgroundThread()
        {
            var manifestPath = Path.Combine(_tempDir, "sync_manifest.json");
            var gameMediaDir = Path.Combine(_tempDir, "media_src");
            var lbRoot = Path.Combine(_tempDir, "lb_root");
            Directory.CreateDirectory(gameMediaDir);
            Directory.CreateDirectory(lbRoot);
            File.WriteAllText(Path.Combine(gameMediaDir, "logo.png"), "logo-bytes");

            File.WriteAllText(
                manifestPath,
                $$"""
                {
                  "ext-100": {
                    "game_id": "ext-100",
                    "game_source": "local",
                    "game_name": "Synced Game",
                    "game_dir": "C:\\Games\\Synced",
                    "platform": "Windows",
                    "is_installed": true,
                    "is_favorite": true,
                    "application_path": "bin\\game.exe",
                    "media": {
                      "media_dir": "{{gameMediaDir.Replace("\\", "\\\\")}}",
                      "image_paths": { "logo": "logo.png" }
                    }
                  }
                }
                """);

            var harness = new FakeDataManagerHarness();
            var refreshCalledOnThreads = new List<int>();

            // Track dispatcher thread cleanly using one persistent UI worker thread
            using var uiQueue = new System.Collections.Concurrent.BlockingCollection<(Action Work, ManualResetEventSlim Done, Exception? Error)>();
            int? dedicatedUiThreadId = null;
            var uiLoop = new Thread(() =>
            {
                dedicatedUiThreadId = Environment.CurrentManagedThreadId;
                foreach (var item in uiQueue.GetConsumingEnumerable())
                {
                    Exception? error = null;
                    try
                    {
                        item.Work();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        uiResults[item.Done] = error;
                        item.Done.Set();
                    }
                }
            });
            uiLoop.Start();

            void DispatchToUiThread(Action action)
            {
                if (Environment.CurrentManagedThreadId == dedicatedUiThreadId)
                {
                    action();
                    return;
                }
                using var done = new ManualResetEventSlim(false);
                uiQueue.Add((action, done, null));
                done.Wait();
                if (uiResults.TryRemove(done, out var err) && err != null)
                {
                    throw err;
                }
            }

            try
            {
                var source = new SourceConfig { ManifestName = "Local", ManifestPath = manifestPath };
                int backgroundThreadId = -1;

                await Task.Run(() =>
                {
                    backgroundThreadId = Environment.CurrentManagedThreadId;
                    SyncEngine.SyncSource(
                        source,
                        syncGames: true,
                        syncMedia: true,
                        progress: null,
                        token: CancellationToken.None,
                        dataManager: harness.Instance,
                        refreshData: () => refreshCalledOnThreads.Add(Environment.CurrentManagedThreadId),
                        uiDispatcher: DispatchToUiThread,
                        mediaRootOverride: lbRoot);
                });

                Assert.NotNull(dedicatedUiThreadId);
                Assert.NotEqual(backgroundThreadId, dedicatedUiThreadId.Value);
                Assert.Equal(1, harness.AddNewGameCount);
                Assert.Equal(1, harness.SaveCount);
                Assert.Single(refreshCalledOnThreads);
                Assert.Equal(dedicatedUiThreadId.Value, refreshCalledOnThreads[0]);
                Assert.NotEmpty(harness.ObservedAccesses);
                Assert.All(harness.ObservedAccesses, access => Assert.Equal(dedicatedUiThreadId.Value, access.ThreadId));

                var savedGame = Assert.Single(harness.Games);
                Assert.Equal("Synced Game", savedGame.Title);
                Assert.True(savedGame.Favorite);
                Assert.Equal(@"C:\Games\Synced\bin\game.exe", savedGame.ApplicationPath);
                Assert.True(File.Exists(Path.Combine(lbRoot, "Images", "Windows", "Clear Logo", $"{savedGame.Id}.png")));
            }
            finally
            {
                uiQueue.CompleteAdding();
                uiLoop.Join();
            }
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<ManualResetEventSlim, Exception?> uiResults =
            new System.Collections.Concurrent.ConcurrentDictionary<ManualResetEventSlim, Exception?>();

        [Fact]
        public void SyncSource_CancelledBeforeCommit_DoesNotMutateOrSaveDataManager()
        {
            var manifestPath = Path.Combine(_tempDir, "cancel_manifest.json");
            File.WriteAllText(
                manifestPath,
                """{"ext-1": {"game_id": "ext-1", "game_name": "Should Not Be Added"}}""");

            var harness = new FakeDataManagerHarness();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var source = new SourceConfig { ManifestName = "Local", ManifestPath = manifestPath };
            Assert.Throws<OperationCanceledException>(() =>
                SyncEngine.SyncSource(
                    source,
                    syncGames: true,
                    syncMedia: false,
                    progress: null,
                    token: cts.Token,
                    dataManager: harness.Instance,
                    refreshData: () => { },
                    uiDispatcher: a => a()));

            Assert.Equal(0, harness.AddNewGameCount);
            Assert.Equal(0, harness.SaveCount);
            Assert.Empty(harness.Games);
        }

        [Fact]
        public void SyncSelectedGames_MatchesCustomGameIdAndUpdatesOnUiThread()
        {
            var manifestPath = Path.Combine(_tempDir, "selected_manifest.json");
            File.WriteAllText(
                manifestPath,
                """
                {
                  "ext-99": {
                    "game_id": "ext-99",
                    "game_source": "local",
                    "game_name": "Updated Selected Game",
                    "metadata": {
                      "developer": "Studio, Inc."
                    }
                  }
                }
                """);

            var existingGame = LaunchBoxFakes.CreateGame(id: "lb-99", title: "Old Title");
            existingGame.Source = "Local";
            var idField = existingGame.AddNewCustomField();
            idField.Name = "Game ID";
            idField.Value = "ext-99";

            var harness = new FakeDataManagerHarness(new[] { existingGame });
            var sources = new List<SourceConfig>
            {
                new SourceConfig { ManifestName = "Local", ManifestPath = manifestPath }
            };

            bool refreshed = false;
            SyncEngine.SyncSelectedGames(
                new[] { existingGame },
                sources,
                syncGames: true,
                syncMedia: false,
                progress: null,
                token: CancellationToken.None,
                dataManager: harness.Instance,
                refreshData: () => refreshed = true,
                uiDispatcher: a => a());

            Assert.Equal("Updated Selected Game", existingGame.Title);
            Assert.Equal("Studio, Inc.", existingGame.Developer);
            Assert.Equal(1, harness.SaveCount);
            Assert.True(refreshed);
        }

        [Fact]
        public void ResolvePath_ResolvesRelativePathsAndPreservesRootedOrUri()
        {
            Assert.Equal(string.Empty, SyncEngine.ResolvePath(null, true, @"C:\Games\Demo"));
            Assert.Equal(string.Empty, SyncEngine.ResolvePath(string.Empty, true, @"C:\Games\Demo"));
            Assert.Equal(@"C:\Abs\game.exe", SyncEngine.ResolvePath(@"C:\Abs\game.exe", true, @"C:\Games\Demo"));
            Assert.Equal("steam://rungameid/10", SyncEngine.ResolvePath("steam://rungameid/10", true, @"C:\Games\Demo"));
            Assert.Equal(@"C:\Games\Demo\bin\run.exe", SyncEngine.ResolvePath("./bin/run.exe", true, @"C:\Games\Demo"));
            Assert.Equal(@"C:\Games\Demo\run.exe", SyncEngine.ResolvePath(@".\run.exe", true, @"C:\Games\Demo"));
            Assert.Equal("bin/run.exe", SyncEngine.ResolvePath("bin/run.exe", false, @"C:\Games\Demo"));
        }

        [Fact]
        public void FormatMetadataField_HandlesArraysCommasNullAndNumbers()
        {
            using var doc = JsonDocument.Parse("""
                {
                  "nullVal": null,
                  "arr": ["Alpha", "", "Beta"],
                  "csv": "Action, RPG, Adventure",
                  "rawCsv": "Studio, Inc.",
                  "num": 42
                }
                """);
            var root = doc.RootElement;

            Assert.Equal(string.Empty, SyncEngine.FormatMetadataField(default));
            Assert.Equal(string.Empty, SyncEngine.FormatMetadataField(root.GetProperty("nullVal")));
            Assert.Equal("Alpha; Beta", SyncEngine.FormatMetadataField(root.GetProperty("arr")));
            Assert.Equal("Action; RPG; Adventure", SyncEngine.FormatMetadataField(root.GetProperty("csv"), splitCommas: true));
            Assert.Equal("Studio, Inc.", SyncEngine.FormatMetadataField(root.GetProperty("rawCsv"), splitCommas: false));
            Assert.Equal("42", SyncEngine.FormatMetadataField(root.GetProperty("num")));
        }

        [Fact]
        public void ParseRole_ExtractsNumericSuffixOrPreservesPlainRole()
        {
            Assert.Equal((string.Empty, string.Empty), SyncEngine.ParseRole(null));
            Assert.Equal((string.Empty, string.Empty), SyncEngine.ParseRole(string.Empty));
            Assert.Equal(("cover", string.Empty), SyncEngine.ParseRole("cover"));
            Assert.Equal(("screenshot", "-03"), SyncEngine.ParseRole("screenshot-03"));
            Assert.Equal(("box-front", string.Empty), SyncEngine.ParseRole("box-front"));
            Assert.Equal(("-02", string.Empty), SyncEngine.ParseRole("-02"));
        }

        [Fact]
        public void UpdateGameData_UpsertsCustomFieldsAndAdditionalApplications()
        {
            var game = LaunchBoxFakes.CreateGame(id: "lb-50", title: "App Test");
            var existingField = game.AddNewCustomField();
            existingField.Name = "Game ID";
            existingField.Value = "old-id";

            var existingApp = game.AddNewAdditionalApplication();
            existingApp.Name = "Manual";
            existingApp.ApplicationPath = "old.pdf";

            var entry = new ManifestEntry
            {
                game_id = "new-id",
                game_source = "gog",
                metadata_source = "igdb",
                metadata_id = "999",
                media_source = "sgdb",
                media_id = "555",
                custom_fields = new List<CustomFieldConfig>
                {
                    new CustomFieldConfig { name = "Origin", value = "Archive" },
                    new CustomFieldConfig { name = "", value = "Ignored" }
                },
                addl_apps = new List<AddlAppConfig>
                {
                    new AddlAppConfig
                    {
                        app_name = "Manual",
                        app_path = "manual.pdf",
                        app_arguments = "--view",
                        auto_run_before = true,
                        auto_run_after = false,
                        wait_for_exit = true,
                        priority = 2
                    },
                    new AddlAppConfig
                    {
                        app_name = "Soundtrack",
                        app_path = "ost.mp3",
                        priority = 5
                    },
                    new AddlAppConfig { app_name = "" }
                }
            };

            SyncEngine.UpdateGameData(game, "new-id", entry);

            var fields = game.GetAllCustomFields().ToDictionary(f => f.Name, f => f.Value);
            Assert.Equal("new-id", fields["Game ID"]);
            Assert.Equal("gog", fields["Game Source"]);
            Assert.Equal("igdb", fields["Metadata Source"]);
            Assert.Equal("999", fields["Metadata ID"]);
            Assert.Equal("sgdb", fields["Media Source"]);
            Assert.Equal("555", fields["Media ID"]);
            Assert.Equal("Archive", fields["Origin"]);

            var apps = game.GetAllAdditionalApplications();
            Assert.NotNull(apps);
            Assert.Equal(2, apps!.Length);
            Assert.Equal("manual.pdf", existingApp.ApplicationPath);
            Assert.Equal("--view", existingApp.CommandLine);
            Assert.True(existingApp.AutoRunBefore);
            Assert.False(existingApp.AutoRunAfter);
            Assert.True(existingApp.WaitForExit);
            Assert.Equal(2, existingApp.Priority);
            Assert.Contains(apps, a => a.Name == "Soundtrack" && a.ApplicationPath == "ost.mp3" && a.Priority == 5);
        }

        [Fact]
        public void GetLaunchBoxMediaDir_UsesOverrideWhenProvided()
        {
            var expected = Path.Combine(_tempDir, "Images");
            Assert.Equal(expected, SyncEngine.GetLaunchBoxMediaDir("Images", _tempDir));
        }

        [Fact]
        public void InvokeOnUiThread_ExecutesActionWhenNoApplicationExists()
        {
            bool executed = false;
            SyncEngine.InvokeOnUiThread(() => executed = true);
            Assert.True(executed);
        }

        [Fact]
        public void SyncAll_ThrowsOperationCanceledException_WhenPreCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sources = new List<SourceConfig> { new SourceConfig { ManifestName = "Demo" } };

            Assert.Throws<OperationCanceledException>(() => SyncEngine.SyncAll(sources, null, cts.Token));
        }

        [Fact]
        public void DistManifest_DeserializesAndMapsAllV2Fields()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var distPath = Path.Combine(repoRoot, "manifest.dist.json");
            Assert.True(File.Exists(distPath), $"Expected manifest.dist.json at {distPath}");

            var manifest = SyncEngine.LoadManifest(distPath);
            Assert.True(manifest.TryGetValue("example_game_id", out var entry));
            Assert.NotNull(entry);
            Assert.Equal("local", entry!.game_source);
            Assert.Equal("Example Game", entry.game_folder);
            Assert.Equal("1.0.0", entry.game_version);
            Assert.NotNull(entry.addl_apps);
            var backupApp = Assert.Single(entry.addl_apps!);
            Assert.Equal("Backup Saves", backupApp.app_name);
            Assert.Equal("--source local backuphandler --game-id example_game_id --backup", backupApp.app_arguments);
            Assert.NotNull(entry.media?.asset_sources);

            var game = LaunchBoxFakes.CreateGame(id: entry.global_id!, title: entry.game_name!);
            SyncEngine.UpdateGameData(game, "example_game_id", entry);
            Assert.Equal(@"C:\Games\Example Game\bin\game.exe", game.ApplicationPath);
            Assert.Equal("1.0.0", game.Version);
            Assert.Contains(game.GetAllAdditionalApplications()!, a => a.Name == "Backup Saves");
        }
    }
}
