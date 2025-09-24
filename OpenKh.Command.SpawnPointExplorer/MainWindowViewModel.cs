using OpenKh.Common;
using OpenKh.Kh2;
using OpenKh.Kh2.Ard;
using OpenKh.Command.SpawnPointExplorer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Forms = System.Windows.Forms;

namespace OpenKh.Command.SpawnPointExplorer;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<EnemyCandidateViewModel> _candidates = new();
    private readonly ObservableCollection<GameNodeViewModel> _occurrenceGames = new();

    private SpawnDataSet? _spawnData;
    private IReadOnlyDictionary<string, string> _modelIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private int _selectionVersion;
    private EnemyCandidateViewModel? _selectedCandidate;
    private string? _dataRoot;
    private string _statusMessage = "Select a data directory and load spawn data.";
    private string _previewStatus = "";
    private string _occurrenceSummary = "";
    private string _selectedCandidateDetails = "";
    private UIElement? _previewContent;
    private bool _isBusy;

    public MainWindowViewModel()
    {
        ExportSelectionCommand = new AsyncRelayCommand(ExportSelectionAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EnemyCandidateViewModel> Candidates => _candidates;
    public ObservableCollection<GameNodeViewModel> OccurrenceRegions => _occurrenceGames;

    public ICommand ExportSelectionCommand { get; }

    public string? DataRoot
    {
        get => _dataRoot;
        set => SetProperty(ref _dataRoot, value);
    }

    public EnemyCandidateViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
            {
                var version = Interlocked.Increment(ref _selectionVersion);
                _ = UpdateSelectionAsync(value, version);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        private set => SetProperty(ref _previewStatus, value);
    }

    public string OccurrenceSummary
    {
        get => _occurrenceSummary;
        private set => SetProperty(ref _occurrenceSummary, value);
    }

    public string SelectedCandidateDetails
    {
        get => _selectedCandidateDetails;
        private set => SetProperty(ref _selectedCandidateDetails, value);
    }

    public UIElement? PreviewContent
    {
        get => _previewContent;
        private set => SetProperty(ref _previewContent, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public object? SelectedTreeItem { get; set; }

    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(DataRoot))
        {
            StatusMessage = "Enter the extracted data directory before loading.";
            return;
        }

        var absolutePath = GetAbsolutePath(DataRoot);
        if (!Directory.Exists(absolutePath))
        {
            StatusMessage = string.Format(CultureInfo.InvariantCulture, "Directory '{0}' does not exist.", absolutePath);
            return;
        }

        DataRoot = absolutePath;
        IsBusy = true;
        StatusMessage = "Scanning spawnpoints...";
        SelectedCandidate = null;
        Candidates.Clear();
        OccurrenceRegions.Clear();
        PreviewContent = null;
        PreviewStatus = string.Empty;
        OccurrenceSummary = string.Empty;
        SelectedCandidateDetails = string.Empty;
        _spawnData = null;
        _modelIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var result = await Task.Run(() => LoadInternal(absolutePath));
            _spawnData = result.SpawnData;
            _modelIndex = result.ModelIndex;

            Candidates.Clear();
            foreach (var candidate in _spawnData.CreateCandidates(result.ObjEntries))
            {
                var viewModel = EnemyCandidateViewModel.FromCandidate(candidate, _modelIndex);
                Candidates.Add(viewModel);
            }

            if (Candidates.Count == 0)
            {
                StatusMessage = "No enemies were discovered in spawn data.";
                return;
            }

            StatusMessage = BuildStatusMessage(result);
            SelectedCandidate = Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load spawn data: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportSelectionAsync(object? selection)
    {
        if (selection == null)
        {
            StatusMessage = "Select a game, region, map, spawn group, or spawn point to export.";
            return;
        }

        if (_spawnData == null)
        {
            StatusMessage = "Load spawn data before exporting.";
            return;
        }

        switch (selection)
        {
            case GameNodeViewModel gameNode:
                await ExportGameAsync(gameNode);
                return;
            case RegionNodeViewModel regionNode:
                await ExportRegionAsync(regionNode);
                return;
            case MapNodeViewModel mapNode:
                await ExportMapAsync(mapNode);
                return;
            case SpawnGroupNodeViewModel groupNode:
                await ExportSpawnGroupAsync(groupNode);
                return;
            case SpawnPointNodeViewModel pointNode:
                await ExportSpawnPointAsync(pointNode);
                return;
            default:
                StatusMessage = "The selected item cannot be exported.";
                return;
        }
    }

    private async Task UpdateSelectionAsync(EnemyCandidateViewModel? candidate, int version)
    {
        if (_spawnData == null || candidate == null)
        {
            OccurrenceRegions.Clear();
            PreviewContent = null;
            PreviewStatus = string.Empty;
            OccurrenceSummary = string.Empty;
            SelectedCandidateDetails = string.Empty;
            return;
        }

        SelectedCandidateDetails = candidate.GetDetails();

        var occurrences = await Task.Run(() => _spawnData.FindEnemyOccurrences(candidate.Id));
        if (version != _selectionVersion)
        {
            return;
        }

        OccurrenceRegions.Clear();
        var defaultGame = GetDefaultGameKey(_spawnData.RootPath);
        foreach (var game in BuildGameNodes(occurrences, defaultGame))
        {
            OccurrenceRegions.Add(game);
        }

        OccurrenceSummary = occurrences.Count == 0
            ? "The selected enemy does not appear in any spawn group."
            : string.Format(CultureInfo.InvariantCulture, "Found in {0} map(s).", occurrences.Count);

        var modelPath = candidate.ModelPath;
        if (string.IsNullOrEmpty(modelPath))
        {
            PreviewContent = null;
            PreviewStatus = string.IsNullOrEmpty(candidate.ModelName)
                ? "The selected entry does not reference a model name in objentry."
                : string.Format(CultureInfo.InvariantCulture, "Model '{0}' not found under kh2/obj.", candidate.ModelName);
            return;
        }

        PreviewStatus = "Loading model preview...";
        var preview = await Task.Run(() => MdlxPreviewBuilder.Load(modelPath!));
        if (version != _selectionVersion)
        {
            return;
        }

        if (preview.Meshes != null)
        {
            try
            {
                PreviewContent = new MdlxViewportControl(preview.Meshes.ToList());
                PreviewStatus = preview.StatusMessage;
            }
            catch (Exception ex)
            {
                PreviewContent = null;
                PreviewStatus = "Failed to display model preview: " + ex.Message;
            }
        }
        else
        {
            PreviewContent = null;
            PreviewStatus = preview.StatusMessage;
        }
    }

    private static string GetAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private async Task ExportGameAsync(GameNodeViewModel gameNode)
    {
        var exports = gameNode.Regions
            .SelectMany(region => region.Maps.SelectMany(map => map.SpawnGroups.Select(group => new SpawnGroupExportItem(map, group))))
            .ToList();

        await ExportSpawnGroupsAsync(
            exports,
            string.Format(CultureInfo.InvariantCulture, "Select the export root directory for game {0}", gameNode.Label),
            exportRoot =>
            {
                var mapCount = exports
                    .Select(item => item.Map.Source.MapName ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var regionCount = exports
                    .Select(item => item.Map.Parent.FullLabel ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Exported {0} spawn group(s) across {1} map(s) in {2} region(s) for game {3} to {4}.",
                    exports.Count,
                    mapCount,
                    regionCount,
                    gameNode.Label,
                    exportRoot);
            });
    }

    private async Task ExportRegionAsync(RegionNodeViewModel regionNode)
    {
        var exports = regionNode.Maps
            .SelectMany(map => map.SpawnGroups.Select(group => new SpawnGroupExportItem(map, group)))
            .ToList();

        await ExportSpawnGroupsAsync(
            exports,
            string.Format(CultureInfo.InvariantCulture, "Select the export root directory for region {0}", regionNode.FullLabel),
            exportRoot =>
            {
                var mapCount = exports
                    .Select(item => item.Map.Source.MapName ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Exported {0} spawn group(s) across {1} map(s) for region {2} to {3}.",
                    exports.Count,
                    mapCount,
                    regionNode.FullLabel,
                    exportRoot);
            });
    }

    private async Task ExportMapAsync(MapNodeViewModel mapNode)
    {
        var exports = mapNode.SpawnGroups
            .Select(group => new SpawnGroupExportItem(mapNode, group))
            .ToList();

        await ExportSpawnGroupsAsync(
            exports,
            string.Format(CultureInfo.InvariantCulture, "Select the export directory for map {0}", mapNode.Source.MapName),
            exportRoot => string.Format(
                CultureInfo.InvariantCulture,
                "Exported {0} spawn group(s) for map {1} to {2}.",
                exports.Count,
                mapNode.Source.MapName,
                exportRoot));
    }

    private async Task ExportSpawnGroupAsync(SpawnGroupNodeViewModel groupNode)
    {
        var mapName = string.IsNullOrWhiteSpace(groupNode.Parent.Source.MapName)
            ? "map"
            : groupNode.Parent.Source.MapName;
        var suggestedName = SanitizeFileName(mapName, "map") + "_" +
            SanitizeFileName(groupNode.Source.SpawnName) + ".yml";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YAML files (*.yml)|*.yml|YAML files (*.yaml)|*.yaml|All files (*.*)|*.*",
            DefaultExt = ".yml",
            FileName = suggestedName,
            AddExtension = true,
        };

        var owner = Application.Current?.MainWindow;
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            await Task.Run(() => WriteSpawnGroupYaml(dialog.FileName, groupNode.Source.AllSpawnPoints));
            StatusMessage = string.Format(CultureInfo.InvariantCulture, "Exported {0}.", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to export YAML: " + ex.Message;
        }
    }

    private async Task ExportSpawnPointAsync(SpawnPointNodeViewModel pointNode)
    {
        var mapName = string.IsNullOrWhiteSpace(pointNode.Parent.Parent.Source.MapName)
            ? "map"
            : pointNode.Parent.Parent.Source.MapName;
        var spawnGroupName = SanitizeFileName(pointNode.Parent.Source.SpawnName);
        var spawnId = pointNode.Source.Spawn.Id.ToString("X04", CultureInfo.InvariantCulture);
        var suggestedName = SanitizeFileName(mapName, "map") + "_" + spawnGroupName + "_" + spawnId + ".yml";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YAML files (*.yml)|*.yml|YAML files (*.yaml)|*.yaml|All files (*.*)|*.*",
            DefaultExt = ".yml",
            FileName = suggestedName,
            AddExtension = true,
        };

        var owner = Application.Current?.MainWindow;
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            var spawnPoints = new List<SpawnPoint> { pointNode.Source.Spawn };
            await Task.Run(() => WriteSpawnGroupYaml(dialog.FileName, spawnPoints));
            StatusMessage = string.Format(CultureInfo.InvariantCulture, "Exported {0}.", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to export YAML: " + ex.Message;
        }
    }

    private async Task ExportSpawnGroupsAsync(
        IReadOnlyList<SpawnGroupExportItem> exports,
        string description,
        Func<string, string> successMessageFactory)
    {
        if (exports.Count == 0)
        {
            StatusMessage = "No spawn groups to export.";
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description
        };

        if (!string.IsNullOrEmpty(DataRoot) && Directory.Exists(DataRoot))
        {
            dialog.SelectedPath = DataRoot;
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var exportRoot = dialog.SelectedPath;

        try
        {
            await Task.Run(() => ExportSpawnGroups(exportRoot, exports));
            StatusMessage = successMessageFactory(exportRoot);
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to export YAML: " + ex.Message;
        }
    }

    private static void ExportSpawnGroups(string exportRoot, IReadOnlyList<SpawnGroupExportItem> exports)
    {
        foreach (var export in exports)
        {
            var directory = GetMapExportDirectory(exportRoot, export.Map.Parent, export.Map.Source);
            Directory.CreateDirectory(directory);

            var fileName = SanitizeFileName(export.Group.Source.SpawnName) + ".yml";
            var filePath = Path.Combine(directory, fileName);
            WriteSpawnGroupYaml(filePath, export.Group.Source.AllSpawnPoints);
        }
    }

    private static void WriteSpawnGroupYaml(string filePath, IEnumerable<SpawnPoint> spawnPoints)
    {
        var yaml = Helpers.YamlSerialize(spawnPoints);
        File.WriteAllText(filePath, yaml);
    }

    private static string GetMapExportDirectory(string exportRoot, RegionNodeViewModel region, MapEnemyOccurrences map)
    {
        var path = ParsePathSegments(map.RelativePath, region.Parent.Key);
        var segments = new List<string> { exportRoot };

        var regionSegment = string.IsNullOrEmpty(path.Region) ? region.Key : path.Region;
        if (!string.IsNullOrWhiteSpace(regionSegment))
        {
            segments.Add(SanitizeFileName(regionSegment, "region"));
        }

        segments.Add("ard");
        var mapSegment = string.IsNullOrWhiteSpace(map.MapName) ? "map" : map.MapName;
        segments.Add(SanitizeFileName(mapSegment, "map"));

        return Path.Combine(segments.ToArray());
    }

    private static IEnumerable<GameNodeViewModel> BuildGameNodes(
        IReadOnlyList<MapEnemyOccurrences> occurrences,
        string defaultGame)
    {
        return occurrences
            .Select(map => new { Map = map, Segments = ParsePathSegments(map.RelativePath, defaultGame) })
            .GroupBy(item => item.Segments.Game, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var gameNode = new GameNodeViewModel(group.Key);
                var regionGroups = group
                    .GroupBy(item => item.Segments.Region, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(regionGroup => regionGroup.Key, StringComparer.OrdinalIgnoreCase);

                foreach (var regionGroup in regionGroups)
                {
                    var regionMaps = regionGroup
                        .Select(item => item.Map)
                        .OrderBy(map => map.MapName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var regionNode = new RegionNodeViewModel(gameNode, regionGroup.Key, regionMaps);
                    gameNode.Regions.Add(regionNode);
                }

                return gameNode;
            });
    }

    private static PathSegments ParsePathSegments(string? relativePath, string defaultGame)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return new PathSegments(defaultGame, string.Empty);
        }

        var normalized = relativePath.Replace("\\", "/");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return new PathSegments(defaultGame, string.Empty);
        }

        var ardIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, "ard", StringComparison.OrdinalIgnoreCase));

        string game = defaultGame;
        string region = string.Empty;

        if (ardIndex >= 0)
        {
            if (ardIndex > 0)
            {
                game = segments[ardIndex - 1];
            }
            else if (segments.Length > 0 && string.IsNullOrEmpty(game))
            {
                game = segments[0];
            }

            var regionIndex = ardIndex + 1;
            if (regionIndex < segments.Length)
            {
                region = segments[regionIndex];
            }
        }
        else
        {
            game = segments[0];
            if (segments.Length > 1)
            {
                region = segments[1];
            }
        }

        return new PathSegments(game, region);
    }

    private readonly struct PathSegments
    {
        public PathSegments(string game, string region)
        {
            Game = game ?? string.Empty;
            Region = region ?? string.Empty;
        }

        public string Game { get; }
        public string Region { get; }
    }

    private static string GetDefaultGameKey(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static LoadResult LoadInternal(string rootPath)
    {
        var spawnData = SpawnDataSet.Build(rootPath);
        var objEntries = LoadObjEntries(rootPath);
        var modelIndex = BuildModelIndex(rootPath);
        return new LoadResult(spawnData, objEntries, modelIndex, spawnData.Issues.ToList());
    }

    private static IReadOnlyDictionary<uint, Objentry> LoadObjEntries(string rootPath)
    {
        var map = new Dictionary<uint, Objentry>();
        var objentryPath = Directory.EnumerateFiles(rootPath, "00objentry.bin", SearchOption.AllDirectories).FirstOrDefault();
        if (objentryPath == null)
        {
            return map;
        }

        using var stream = File.OpenRead(objentryPath);
        foreach (var entry in Objentry.Read(stream))
        {
            map[(uint)entry.ObjectId] = entry;
        }

        return map;
    }

    private static IReadOnlyDictionary<string, string> BuildModelIndex(string rootPath)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var objDirectory = Path.Combine(rootPath, "kh2", "obj");
        if (!Directory.Exists(objDirectory))
        {
            return dictionary;
        }

        foreach (var mdlxPath in Directory.EnumerateFiles(objDirectory, "*.mdlx", SearchOption.AllDirectories))
        {
            var key = Path.GetFileNameWithoutExtension(mdlxPath);
            if (!dictionary.ContainsKey(key))
            {
                dictionary[key] = mdlxPath;
            }
        }

        return dictionary;
    }

    private static string BuildStatusMessage(LoadResult result)
    {
        var mapCount = result.SpawnData.Maps.Count;
        var enemyCount = result.SpawnData.ObjectCounts.Count;
        var issuesSuffix = result.Issues.Count == 0
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, " {0} file(s) failed to load.", result.Issues.Count);
        var objentrySuffix = result.ObjEntries.Count == 0
            ? " Enemy names unavailable (00objentry.bin not found)."
            : string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Loaded {0} map(s) with {1} distinct enemy object(s).{2}{3}",
            mapCount,
            enemyCount,
            issuesSuffix,
            objentrySuffix);
    }

    private static string SanitizeFileName(string name, string fallback = "spawn")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting)
            {
                return false;
            }

            return _canExecute?.Invoke(parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        private void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record LoadResult(
        SpawnDataSet SpawnData,
        IReadOnlyDictionary<uint, Objentry> ObjEntries,
        IReadOnlyDictionary<string, string> ModelIndex,
        List<SpawnScanIssue> Issues);

    private sealed record SpawnGroupExportItem(MapNodeViewModel Map, SpawnGroupNodeViewModel Group);
}

internal sealed record EnemyCandidateViewModel(
    uint Id,
    string DisplayName,
    string? ModelName,
    Objentry.Type? Type,
    int OccurrenceCount,
    string? ModelPath)
{
    public static EnemyCandidateViewModel FromCandidate(EnemyCandidate candidate, IReadOnlyDictionary<string, string> modelIndex)
    {
        var modelName = candidate.CleanModelName;
        string? modelPath = null;
        if (!string.IsNullOrEmpty(modelName))
        {
            modelIndex.TryGetValue(modelName, out modelPath);
        }
        var displayName = string.IsNullOrWhiteSpace(modelName)
            ? string.Format(CultureInfo.InvariantCulture, "0x{0:X04}", candidate.Id)
            : string.Format(CultureInfo.InvariantCulture, "{0} (0x{1:X04})", modelName, candidate.Id);
        return new EnemyCandidateViewModel(candidate.Id, displayName, modelName, candidate.Type, candidate.OccurrenceCount, modelPath);
    }

    public string GetDetails()
    {
        var typeLabel = Type.HasValue ? GetTypeDescription(Type.Value) : "Unknown";
        return string.Format(CultureInfo.InvariantCulture, "ID 0x{0:X04} • Type {1} • Occurrences {2}", Id, typeLabel, OccurrenceCount);
    }

    private static string GetTypeDescription(Objentry.Type type)
    {
        var member = typeof(Objentry.Type).GetMember(type.ToString()).FirstOrDefault();
        if (member == null)
        {
            return type.ToString();
        }

        var description = member.GetCustomAttributes(typeof(DescriptionAttribute), false).OfType<DescriptionAttribute>().FirstOrDefault();
        return description?.Description ?? type.ToString();
    }
}

internal sealed class GameNodeViewModel
{
    public GameNodeViewModel(string key)
    {
        Key = key ?? string.Empty;
        Regions = new ObservableCollection<RegionNodeViewModel>();
    }

    public string Key { get; }

    public string Label => string.IsNullOrWhiteSpace(Key) ? "(root)" : Key.ToUpperInvariant();

    public ObservableCollection<RegionNodeViewModel> Regions { get; }

    public string DisplayName => Label;
}

internal sealed class RegionNodeViewModel
{
    public RegionNodeViewModel(GameNodeViewModel parent, string key, IReadOnlyList<MapEnemyOccurrences> maps)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Key = key ?? string.Empty;
        Maps = new ObservableCollection<MapNodeViewModel>(maps.Select(map => new MapNodeViewModel(this, map)));
    }

    public GameNodeViewModel Parent { get; }

    public string Key { get; }

    public string Label => string.IsNullOrWhiteSpace(Key) ? "(unknown region)" : Key;

    public string FullLabel => string.IsNullOrWhiteSpace(Key)
        ? Parent.Label
        : string.Format(CultureInfo.InvariantCulture, "{0}-{1}", Parent.Label, Label);

    public ObservableCollection<MapNodeViewModel> Maps { get; }

    public string DisplayName => Label;
}

internal sealed class MapNodeViewModel
{
    public MapNodeViewModel(RegionNodeViewModel parent, MapEnemyOccurrences source)
    {
        Parent = parent;
        Source = source;
        SpawnGroups = new ObservableCollection<SpawnGroupNodeViewModel>(source.SpawnGroups.Select(group => new SpawnGroupNodeViewModel(this, group)));
    }

    public RegionNodeViewModel Parent { get; }

    public MapEnemyOccurrences Source { get; }

    public ObservableCollection<SpawnGroupNodeViewModel> SpawnGroups { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Source.MapName)
        ? "(unknown map)"
        : Source.MapName;
}

internal sealed class SpawnGroupNodeViewModel
{
    public SpawnGroupNodeViewModel(MapNodeViewModel parent, SpawnGroupOccurrences source)
    {
        Parent = parent;
        Source = source;
        SpawnPoints = new ObservableCollection<SpawnPointNodeViewModel>(source.SpawnPoints.Select(point => new SpawnPointNodeViewModel(this, point)));
    }

    public MapNodeViewModel Parent { get; }

    public SpawnGroupOccurrences Source { get; }

    public ObservableCollection<SpawnPointNodeViewModel> SpawnPoints { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Source.SpawnName)
        ? "(unnamed script)"
        : Source.SpawnName;
}

internal sealed class SpawnPointNodeViewModel
{
    public SpawnPointNodeViewModel(SpawnGroupNodeViewModel parent, SpawnPointOccurrences source)
    {
        Parent = parent;
        Source = source;
    }

    public SpawnGroupNodeViewModel Parent { get; }

    public SpawnPointOccurrences Source { get; }

    public string DisplayName
    {
        get
        {
            var spawn = Source.Spawn;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Spawn ID 0x{0:X04} • Type {1} • Flag 0x{2:X2} • {3} match(es)",
                spawn.Id,
                spawn.Type,
                spawn.Flag,
                Source.Entities.Count);
        }
    }
}

