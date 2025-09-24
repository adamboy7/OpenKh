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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenKh.Command.SpawnPointExplorer;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<EnemyCandidateViewModel> _candidates = new();
    private readonly ObservableCollection<MapNodeViewModel> _occurrenceMaps = new();

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
    public ObservableCollection<MapNodeViewModel> OccurrenceMaps => _occurrenceMaps;

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
        OccurrenceMaps.Clear();
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
            StatusMessage = "Select a map, spawn group, or spawn point to export.";
            return;
        }

        if (_spawnData == null)
        {
            StatusMessage = "Load spawn data before exporting.";
            return;
        }

        var export = BuildExport(selection);
        if (export == null)
        {
            StatusMessage = "The selected item cannot be exported.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YAML files (*.yml)|*.yml|YAML files (*.yaml)|*.yaml|All files (*.*)|*.*",
            DefaultExt = ".yml",
            FileName = export.SuggestedFileName,
            AddExtension = true,
        };

        var owner = System.Windows.Application.Current?.MainWindow;
        var result = dialog.ShowDialog(owner);
        if (result != true)
        {
            return;
        }

        try
        {
            await Task.Run(() => WriteYaml(dialog.FileName, export.Model));
            StatusMessage = string.Format(CultureInfo.InvariantCulture, "Exported {0}.", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to export YAML: " + ex.Message;
        }
    }

    private async Task UpdateSelectionAsync(EnemyCandidateViewModel? candidate, int version)
    {
        if (_spawnData == null || candidate == null)
        {
            OccurrenceMaps.Clear();
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

        OccurrenceMaps.Clear();
        foreach (var map in occurrences)
        {
            OccurrenceMaps.Add(new MapNodeViewModel(map));
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

        PreviewContent = preview.Meshes != null
            ? new MdlxViewportControl(preview.Meshes.ToList())
            : null;
        PreviewStatus = preview.StatusMessage;
    }

    private static string GetAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private static void WriteYaml(string filePath, SpawnExportModel model)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        var yaml = serializer.Serialize(model);
        File.WriteAllText(filePath, yaml);
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

    private static ExportResult? BuildExport(object selection)
    {
        switch (selection)
        {
            case MapNodeViewModel mapNode:
                var mapModel = SpawnExportModel.FromMap(mapNode.Source);
                return new ExportResult(mapModel, mapNode.Source.MapName + ".yml");
            case SpawnGroupNodeViewModel groupNode:
                var groupModel = SpawnExportModel.FromGroup(groupNode.Parent.Source, groupNode.Source);
                return new ExportResult(groupModel, groupNode.Parent.Source.MapName + "_" + SanitizeFileName(groupNode.Source.SpawnName) + ".yml");
            case SpawnPointNodeViewModel pointNode:
                var pointModel = SpawnExportModel.FromPoint(pointNode.Parent.Parent.Source, pointNode.Parent.Source, pointNode.Source);
                return new ExportResult(pointModel, pointNode.Parent.Parent.Source.MapName + "_" + SanitizeFileName(pointNode.Parent.Source.SpawnName) + "_" + pointNode.Source.Spawn.Id.ToString("X04", CultureInfo.InvariantCulture) + ".yml");
            default:
                return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "spawn" : safe;
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

    private sealed record ExportResult(SpawnExportModel Model, string SuggestedFileName);
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

internal sealed class MapNodeViewModel
{
    public MapNodeViewModel(MapEnemyOccurrences source)
    {
        Source = source;
        SpawnGroups = new ObservableCollection<SpawnGroupNodeViewModel>(source.SpawnGroups.Select(group => new SpawnGroupNodeViewModel(this, group)));
    }

    public MapEnemyOccurrences Source { get; }

    public ObservableCollection<SpawnGroupNodeViewModel> SpawnGroups { get; }

    public string DisplayName => string.Format(CultureInfo.InvariantCulture, "{0} ({1})", Source.MapName, Source.RelativePath);
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

    public string DisplayName => string.Format(CultureInfo.InvariantCulture, "{0} ({1} spawnpoint(s))", Source.SpawnName, Source.SpawnPoints.Count);
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

internal sealed record SpawnExportModel(string Map, string RelativePath, IReadOnlyList<SpawnExportGroup> Groups)
{
    public static SpawnExportModel FromMap(MapEnemyOccurrences map) =>
        new(map.MapName, map.RelativePath, map.SpawnGroups.Select(group => FromGroupInternal(group)).ToList());

    public static SpawnExportModel FromGroup(MapEnemyOccurrences map, SpawnGroupOccurrences group) =>
        new(map.MapName, map.RelativePath, new[] { FromGroupInternal(group) });

    public static SpawnExportModel FromPoint(MapEnemyOccurrences map, SpawnGroupOccurrences group, SpawnPointOccurrences point) =>
        new(map.MapName, map.RelativePath, new[] { new SpawnExportGroup(group.SpawnName, new[] { FromPointInternal(point) }) });

    private static SpawnExportGroup FromGroupInternal(SpawnGroupOccurrences group) =>
        new(group.SpawnName, group.SpawnPoints.Select(FromPointInternal).ToList());

    private static SpawnExportPoint FromPointInternal(SpawnPointOccurrences point)
    {
        var spawn = point.Spawn;
        return new SpawnExportPoint(
            spawn.Id,
            spawn.Type,
            spawn.Flag,
            spawn.Teleport.Place,
            spawn.Teleport.Door,
            spawn.Teleport.World,
            point.Entities.Select(entity => new SpawnExportEntity(
                entity.ObjectId,
                entity.Serial,
                entity.SpawnType,
                entity.SpawnArgument,
                entity.SpawnDelay,
                entity.SpawnRange,
                entity.PositionX,
                entity.PositionY,
                entity.PositionZ)).ToList());
    }
}

internal sealed record SpawnExportGroup(string Name, IReadOnlyList<SpawnExportPoint> Points);

internal sealed record SpawnExportPoint(
    int SpawnId,
    int SpawnType,
    int SpawnFlag,
    byte Place,
    byte Door,
    byte World,
    IReadOnlyList<SpawnExportEntity> Entities);

internal sealed record SpawnExportEntity(
    int ObjectId,
    short Serial,
    byte SpawnType,
    byte SpawnArgument,
    short SpawnDelay,
    short SpawnRange,
    float PositionX,
    float PositionY,
    float PositionZ);
