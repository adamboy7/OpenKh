using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using OpenKh.Tools.Common.Wpf;
using OpenKh.Tools.ModBrowser.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenKh.Tools.ModBrowser.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<ModEntry> _mods = new();
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, IReadOnlyList<ModBadge>> _badgeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModJsonEntry> _modJsonEntries = new();
    private readonly Dictionary<string, ModJsonEntry> _modJsonEntryMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _modsFileLock = new(1, 1);
    private readonly SemaphoreSlim _followedUsersFileLock = new(1, 1);
    private readonly HashSet<string> _followedUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient = CreateHttpClient();
    private string _searchQuery = string.Empty;
    private SortOption _selectedSortOption;
    private SearchFilters _activeFilters = SearchFilters.Empty;
    private string? _modsFilePath;
    private string? _followedUsersFilePath;

    public enum AddModResult
    {
        Added,
        InvalidInput,
        AlreadyExists,
        NotFound,
        Failed
    }

    public enum FollowUserStatus
    {
        Success,
        InvalidInput,
        NotFound,
        Failed
    }

    public sealed record FollowUserResult(
        FollowUserStatus Status,
        string Username,
        int AddedCount,
        int AlreadyTrackedCount,
        int FailedCount,
        int TotalRepositories);

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        ModsWithIconView = CreateView(ModCategory.WithIcon);
        ModsWithoutIconView = CreateView(ModCategory.WithoutIcon);
        ModsOtherView = CreateView(ModCategory.Other);

        LoadBadgesCommand = new RelayCommand<ModEntry>(entry => _ = LoadBadgesForEntryAsync(entry));
        OpenInBrowserCommand = new RelayCommand<ModEntry>(OpenEntryInBrowser);

        SortOptions = new List<SortOptionInfo>
        {
            new(SortOption.CreationDate, "Creation Date (Newest)"),
            new(SortOption.LastUpdate, "Last Update (Newest)"),
            new(SortOption.AuthorName, "Author Name (A-Z)")
        };

        _selectedSortOption = SortOption.CreationDate;
        ApplySorting();
        LoadMods();
        LoadFollowedUsers();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView ModsWithIconView { get; }

    public ICollectionView ModsWithoutIconView { get; }

    public ICollectionView ModsOtherView { get; }

    public RelayCommand<ModEntry> LoadBadgesCommand { get; }

    public RelayCommand<ModEntry> OpenInBrowserCommand { get; }

    public IReadOnlyList<SortOptionInfo> SortOptions { get; }

    public SortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (_selectedSortOption == value)
            {
                return;
            }

            _selectedSortOption = value;
            OnPropertyChanged(nameof(SelectedSortOption));
            ApplySorting();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value)
            {
                return;
            }

            _searchQuery = value;
            _activeFilters = SearchFilters.Parse(value);
            OnPropertyChanged(nameof(SearchQuery));
            RefreshViews();
        }
    }

    public async Task<AddModResult> AddModAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRepository(input, out var repo))
        {
            return AddModResult.InvalidInput;
        }

        if (_modJsonEntryMap.ContainsKey(repo))
        {
            return AddModResult.AlreadyExists;
        }

        _modsFilePath ??= ResolveModsFilePath() ?? Path.Combine(AppContext.BaseDirectory, "mods.json");

        try
        {
            var metadata = await FetchRepositoryLanguageMetadataAsync(repo, cancellationToken);
            if (metadata == null)
            {
                return AddModResult.NotFound;
            }

            var treePaths = await FetchRepositoryTreeAsync(repo, cancellationToken);
            var hasModYml = treePaths.Any(path => string.Equals(Path.GetFileName(path), "mod.yml", StringComparison.OrdinalIgnoreCase));
            var hasIcon = treePaths.Any(path => string.Equals(Path.GetFileName(path), "icon.png", StringComparison.OrdinalIgnoreCase));
            var modYmlUrl = hasModYml ? BuildRawGitHubUrl(repo, "mod.yml") : null;

            Dictionary<string, ModJsonMatch>? matches = null;
            if (hasModYml || hasIcon)
            {
                matches = new Dictionary<string, ModJsonMatch>(StringComparer.OrdinalIgnoreCase);
                if (hasModYml)
                {
                    matches["mod.yml"] = new ModJsonMatch
                    {
                        Exists = true,
                        Url = modYmlUrl
                    };
                }

                if (hasIcon)
                {
                    matches["icon.png"] = new ModJsonMatch
                    {
                        Exists = true,
                        Url = BuildRawGitHubUrl(repo, "icon.png")
                    };
                }
            }

            var author = metadata.Owner;
            if (string.IsNullOrWhiteSpace(author))
            {
                var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    author = parts[0];
                }
            }

            var jsonEntry = new ModJsonEntry
            {
                Repo = repo,
                Author = author,
                CreatedAt = metadata.CreatedAt,
                LastPush = metadata.LastPush,
                ModYmlUrl = modYmlUrl,
                HasIcon = hasIcon,
                Matches = matches,
                Languages = metadata.Languages
            };

            _modJsonEntries.Add(jsonEntry);
            _modJsonEntryMap[repo] = jsonEntry;

            var category = DetermineCategory(jsonEntry);
            var iconUrl = ExtractIconUrl(jsonEntry);

            var entry = new ModEntry(
                jsonEntry.Repo,
                jsonEntry.Author,
                jsonEntry.CreatedAt,
                jsonEntry.LastPush,
                iconUrl,
                category,
                jsonEntry.ModYmlUrl);

            await _dispatcher.InvokeAsync(() =>
            {
                _mods.Add(entry);
                RefreshViews();
            });
            await PersistModsJsonAsync(cancellationToken);

            return AddModResult.Added;
        }
        catch (HttpRequestException)
        {
            return AddModResult.Failed;
        }
        catch (TaskCanceledException)
        {
            return AddModResult.Failed;
        }
        catch (JsonException)
        {
            return AddModResult.Failed;
        }
    }

    public async Task<FollowUserResult> FollowUserAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUsername(input, out var username))
        {
            return new FollowUserResult(FollowUserStatus.InvalidInput, string.Empty, 0, 0, 0, 0);
        }

        try
        {
            var (repositories, notFound) = await FetchUserRepositoriesAsync(username, cancellationToken).ConfigureAwait(false);
            if (notFound)
            {
                return new FollowUserResult(FollowUserStatus.NotFound, username, 0, 0, 0, 0);
            }

            var uniqueRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var alreadyTracked = 0;
            var failed = 0;

            foreach (var repository in repositories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!uniqueRepositories.Add(repository))
                {
                    continue;
                }

                var result = await AddModAsync(repository, cancellationToken).ConfigureAwait(false);
                switch (result)
                {
                    case AddModResult.Added:
                        added++;
                        break;
                    case AddModResult.AlreadyExists:
                        alreadyTracked++;
                        break;
                    case AddModResult.InvalidInput:
                    case AddModResult.NotFound:
                    case AddModResult.Failed:
                        failed++;
                        break;
                }
            }

            await RegisterFollowedUserAsync(username, cancellationToken).ConfigureAwait(false);

            return new FollowUserResult(FollowUserStatus.Success, username, added, alreadyTracked, failed, uniqueRepositories.Count);
        }
        catch (HttpRequestException)
        {
            return new FollowUserResult(FollowUserStatus.Failed, username, 0, 0, 0, 0);
        }
        catch (OperationCanceledException)
        {
            return new FollowUserResult(FollowUserStatus.Failed, username, 0, 0, 0, 0);
        }
        catch (JsonException)
        {
            return new FollowUserResult(FollowUserStatus.Failed, username, 0, 0, 0, 0);
        }
    }

    private ListCollectionView CreateView(ModCategory category)
    {
        var view = new ListCollectionView(_mods)
        {
            Filter = item => item is ModEntry entry && entry.Category == category && MatchesSearch(entry)
        };

        return view;
    }

    private void RefreshViews()
    {
        ModsWithIconView.Refresh();
        ModsWithoutIconView.Refresh();
        ModsOtherView.Refresh();
    }

    private void ApplySorting()
    {
        void ApplySort(ICollectionView view)
        {
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                switch (_selectedSortOption)
                {
                    case SortOption.CreationDate:
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.CreatedAt), ListSortDirection.Descending));
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Name), ListSortDirection.Ascending));
                        break;
                    case SortOption.LastUpdate:
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.LastPush), ListSortDirection.Descending));
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Name), ListSortDirection.Ascending));
                        break;
                    case SortOption.AuthorName:
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Author), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.Name), ListSortDirection.Ascending));
                        break;
                }
            }
        }

        ApplySort(ModsWithIconView);
        ApplySort(ModsWithoutIconView);
        ApplySort(ModsOtherView);
    }

    private void LoadMods()
    {
        var modsFilePath = ResolveModsFilePath();
        if (modsFilePath == null)
        {
            return;
        }

        _modsFilePath = modsFilePath;

        try
        {
            var json = File.ReadAllText(modsFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var entries = JsonSerializer.Deserialize<List<ModJsonEntry>>(json, options) ?? new List<ModJsonEntry>();

            _mods.Clear();
            _badgeCache.Clear();
            _modJsonEntries.Clear();
            _modJsonEntryMap.Clear();
            foreach (var entry in entries)
            {
                _modJsonEntries.Add(entry);
                if (!string.IsNullOrWhiteSpace(entry.Repo))
                {
                    _modJsonEntryMap[entry.Repo] = entry;
                }

                var category = DetermineCategory(entry);
                var iconUrl = ExtractIconUrl(entry);
                var modYmlUrl = ExtractModYmlUrl(entry);
                _mods.Add(new ModEntry(
                    entry.Repo,
                    entry.Author,
                    entry.CreatedAt,
                    entry.LastPush,
                    iconUrl,
                    category,
                    modYmlUrl));

            }

            RefreshViews();
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException)
        {
            // Swallow and keep list empty. In a real app we might log this.
        }
    }

    private void LoadFollowedUsers()
    {
        var filePath = ResolveFollowedUsersFilePath() ?? Path.Combine(AppContext.BaseDirectory, "followed-users.txt");
        _followedUsersFilePath = filePath;

        try
        {
            if (File.Exists(filePath))
            {
                _followedUsers.Clear();
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var normalized = line.Trim();
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        _followedUsers.Add(normalized);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Ignore errors while loading followed users.
        }
    }

    private void OpenEntryInBrowser(ModEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        var repo = entry.Repo?.Trim();
        if (string.IsNullOrEmpty(repo))
        {
            return;
        }

        if (!Uri.TryCreate(repo, UriKind.Absolute, out var uri))
        {
            var sanitized = repo;
            if (sanitized.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = sanitized["github.com/".Length..];
            }

            sanitized = sanitized.TrimStart('/');

            if (!Uri.TryCreate($"https://github.com/{sanitized}", UriKind.Absolute, out uri))
            {
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ignore failures when launching the browser.
        }
    }

    private async Task LoadBadgesForEntryAsync(ModEntry? entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            return;
        }

        if (_badgeCache.TryGetValue(entry.Repo, out var cached))
        {
            entry.UpdateBadges(cached);
            return;
        }

        if (entry.IsLoadingBadges)
        {
            return;
        }

        try
        {
            entry.SetLoadingBadges(true);
            var result = await QueryBadgesAsync(entry, cancellationToken);
            _badgeCache[entry.Repo] = result.Badges;
            entry.UpdateBadges(result.Badges);
            await ApplyMetadataUpdatesAsync(entry, result.CreatedAt, result.LastPush, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Ignore connectivity issues; badges will remain hidden.
        }
        catch (TaskCanceledException)
        {
            // Swallow cancellation.
        }
        catch (JsonException)
        {
            // Ignore malformed responses.
        }
        catch (YamlException)
        {
            // Ignore malformed YAML payloads.
        }
        finally
        {
            entry.SetLoadingBadges(false);
        }
    }

    private async Task<BadgeQueryResult> QueryBadgesAsync(ModEntry entry, CancellationToken cancellationToken)
    {
        var badges = new List<ModBadge>();
        DateTime? createdAt = null;
        DateTime? lastPush = null;

        var languageMetadata = await FetchRepositoryLanguageMetadataAsync(entry.Repo, cancellationToken).ConfigureAwait(false);
        if (languageMetadata != null)
        {
            if (languageMetadata.HasLua)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateLua());
            }

            createdAt = languageMetadata.CreatedAt;
            lastPush = languageMetadata.LastPush;
        }

        var treePaths = await FetchRepositoryTreeAsync(entry.Repo, cancellationToken).ConfigureAwait(false);
        if (treePaths.Count > 0)
        {
            if (ContainsLuaFiles(treePaths))
            {
                AddBadgeIfMissing(badges, ModBadge.CreateLua());
            }

            if (ContainsRemasteredEntries(treePaths))
            {
                AddBadgeIfMissing(badges, ModBadge.CreateHd());
            }
        }

        var modYamlContent = await FetchModYamlAsync(entry, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(modYamlContent))
        {
            var analysis = AnalyzeModYaml(modYamlContent);
            if (analysis.TargetsPc)
            {
                AddBadgeIfMissing(badges, ModBadge.CreatePc());
            }

            if (analysis.TargetsPs2)
            {
                AddBadgeIfMissing(badges, ModBadge.CreatePs2());
            }

            if (analysis.TargetsKh1)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateKh1());
            }

            if (analysis.TargetsKh2)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateKh2());
            }

            if (analysis.TargetsBbs)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateBbs());
            }

            if (analysis.TargetsRecom)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateRecom());
            }

            if (analysis.WritesRemasteredAssets)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateHd());
            }

            if (analysis.UsesHeavyCopy)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateHeavy());
            }

            if (analysis.HasCopyExtensionMismatch)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateSpooky());
            }
        }

        var badgeArray = badges.Count > 0 ? badges.ToArray() : Array.Empty<ModBadge>();
        return new BadgeQueryResult(badgeArray, createdAt, lastPush);
    }

    private async Task<(List<string> repositories, bool notFound)> FetchUserRepositoriesAsync(string username, CancellationToken cancellationToken)
    {
        var repositories = new List<string>();
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await _httpClient
                .GetAsync($"https://api.github.com/users/{username}/repos?per_page=100&page={page}", cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (new List<string>(), true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"GitHub API responded with status {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                count++;
                if (element.TryGetProperty("full_name", out var fullNameProperty))
                {
                    var fullName = fullNameProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(fullName))
                    {
                        repositories.Add(fullName.Trim());
                    }
                }
            }

            if (count < 100)
            {
                break;
            }

            page++;
        }

        return (repositories, false);
    }

    private async Task<RepositoryLanguageMetadata?> FetchRepositoryLanguageMetadataAsync(string repo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
        {
            return null;
        }

        var hasLua = false;
        Dictionary<string, long>? languages = null;

        using (var response = await _httpClient
            .GetAsync($"https://api.github.com/repos/{repo}/languages", cancellationToken)
            .ConfigureAwait(false))
        {
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                languages = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "Lua", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLua = true;
                    }

                    long value = 0;
                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        if (property.Value.TryGetInt64(out var longValue))
                        {
                            value = longValue;
                        }
                        else if (property.Value.TryGetDouble(out var doubleValue))
                        {
                            value = (long)doubleValue;
                        }
                    }

                    languages[property.Name] = value;
                }
            }
        }

        DateTime? createdAt = null;
        DateTime? lastPush = null;
        string? owner = null;

        using (var response = await _httpClient
            .GetAsync($"https://api.github.com/repos/{repo}", cancellationToken)
            .ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            if (root.TryGetProperty("created_at", out var createdAtElement) && createdAtElement.ValueKind == JsonValueKind.String)
            {
                createdAt = ParseGitHubDate(createdAtElement.GetString());
            }

            if (root.TryGetProperty("pushed_at", out var pushedAtElement) && pushedAtElement.ValueKind == JsonValueKind.String)
            {
                lastPush = ParseGitHubDate(pushedAtElement.GetString());
            }

            if (root.TryGetProperty("owner", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.Object)
            {
                if (ownerElement.TryGetProperty("login", out var loginElement) && loginElement.ValueKind == JsonValueKind.String)
                {
                    owner = loginElement.GetString();
                }
            }
        }

        return new RepositoryLanguageMetadata(hasLua, createdAt, lastPush, languages, owner);
    }

    private async Task<List<string>> FetchRepositoryTreeAsync(string repo, CancellationToken cancellationToken)
    {
        var paths = new List<string>();

        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
        {
            return paths;
        }

        using var response = await _httpClient
            .GetAsync($"https://api.github.com/repos/{repo}/git/trees/HEAD?recursive=1", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return paths;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("tree", out var treeElement))
        {
            return paths;
        }

        foreach (var item in treeElement.EnumerateArray())
        {
            if (!item.TryGetProperty("path", out var pathElement))
            {
                continue;
            }

            var path = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private async Task<string?> FetchModYamlAsync(ModEntry entry, CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.ModYmlUrl))
        {
            urls.Add(entry.ModYmlUrl);
        }

        var fallback = BuildRawGitHubUrl(entry.Repo, "mod.yml");
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            urls.Add(fallback);
        }

        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private Task ApplyMetadataUpdatesAsync(ModEntry entry, DateTime? createdAt, DateTime? lastPush, CancellationToken cancellationToken)
    {
        if (createdAt == null && lastPush == null)
        {
            return Task.CompletedTask;
        }

        var changed = entry.UpdateDates(createdAt, lastPush);
        if (!changed)
        {
            return Task.CompletedTask;
        }

        if (_modJsonEntryMap.TryGetValue(entry.Repo, out var jsonEntry))
        {
            jsonEntry.CreatedAt = entry.CreatedAt;
            jsonEntry.LastPush = entry.LastPush;
            return PersistModsJsonAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task PersistModsJsonAsync(CancellationToken cancellationToken)
    {
        if (_modsFilePath == null || _modJsonEntries.Count == 0)
        {
            return;
        }

        await _modsFileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            await using var stream = new FileStream(
                _modsFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true);

            await JsonSerializer.SerializeAsync(stream, _modJsonEntries, options, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Ignore failures while attempting to sync metadata locally.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission issues during background persistence.
        }
        catch (NotSupportedException)
        {
            // Ignore unsupported file scenarios.
        }
        catch (JsonException)
        {
            // Ignore serialization errors; the UI still reflects the updated data.
        }
        finally
        {
            _modsFileLock.Release();
        }
    }

    private async Task RegisterFollowedUserAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        if (!_followedUsers.Add(username))
        {
            return;
        }

        _followedUsersFilePath ??= ResolveFollowedUsersFilePath() ?? Path.Combine(AppContext.BaseDirectory, "followed-users.txt");
        await PersistFollowedUsersAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistFollowedUsersAsync(CancellationToken cancellationToken)
    {
        if (_followedUsersFilePath == null)
        {
            return;
        }

        await _followedUsersFileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_followedUsersFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var ordered = _followedUsers
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await File.WriteAllLinesAsync(_followedUsersFilePath, ordered, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Ignore failures while attempting to sync followed users locally.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission issues while persisting followed users.
        }
        finally
        {
            _followedUsersFileLock.Release();
        }
    }

    private static bool ContainsLuaFiles(IEnumerable<string> paths) =>
        paths.Any(path =>
            path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "lua", StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsRemasteredEntries(IEnumerable<string> paths) =>
        paths.Any(path => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "remastered", StringComparison.OrdinalIgnoreCase)));

    private static void AddBadgeIfMissing(List<ModBadge> badges, ModBadge badge)
    {
        if (badges.Any(existing => existing.Equals(badge)))
        {
            return;
        }

        badges.Add(badge);
    }

    private static ModYamlAnalysis AnalyzeModYaml(string yaml)
    {
        var analysis = new ModYamlAnalysis();

        var yamlStream = new YamlStream();
        using var reader = new StringReader(yaml);
        yamlStream.Load(reader);

        if (yamlStream.Documents.Count == 0)
        {
            return analysis;
        }

        var root = yamlStream.Documents[0].RootNode;
        if (root != null)
        {
            AnalyzeYamlNode(root, analysis);
        }

        return analysis;
    }

    private static void AnalyzeYamlNode(YamlNode node, ModYamlAnalysis analysis)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                string? method = null;
                string? name = null;
                YamlNode? sourceNode = null;

                foreach (var child in mapping.Children)
                {
                    var key = (child.Key as YamlScalarNode)?.Value;

                    if (string.Equals(key, "method", StringComparison.OrdinalIgnoreCase))
                    {
                        method = (child.Value as YamlScalarNode)?.Value;
                    }
                    else if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    {
                        name = (child.Value as YamlScalarNode)?.Value;
                    }
                    else if (string.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceNode = child.Value;
                    }

                    if (child.Value is YamlScalarNode scalarValue && ContainsRemasteredSegment(scalarValue.Value))
                    {
                        analysis.WritesRemasteredAssets = true;
                    }

                    if (string.Equals(key, "platform", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "platforms", StringComparison.OrdinalIgnoreCase))
                    {
                        CollectPlatformInfo(child.Value, analysis);
                    }
                    else if (string.Equals(key, "game", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(key, "collectionGames", StringComparison.OrdinalIgnoreCase))
                    {
                        CollectGameInfo(child.Value, analysis);
                    }

                    AnalyzeYamlNode(child.Value, analysis);
                }

                if (string.Equals(method, "copy", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(name) &&
                    (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".bar", StringComparison.OrdinalIgnoreCase)))
                {
                    analysis.UsesHeavyCopy = true;
                }

                if (string.Equals(method, "copy", StringComparison.OrdinalIgnoreCase) &&
                    HasCopyExtensionMismatch(name, sourceNode))
                {
                    analysis.HasCopyExtensionMismatch = true;
                }

                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence)
                {
                    AnalyzeYamlNode(child, analysis);
                }

                break;
        }
    }

    private static void CollectPlatformInfo(YamlNode node, ModYamlAnalysis analysis)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                UpdatePlatformFlags(scalar.Value, analysis);
                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence)
                {
                    CollectPlatformInfo(child, analysis);
                }

                break;
        }
    }

    private static void CollectGameInfo(YamlNode node, ModYamlAnalysis analysis)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                UpdateGameFlags(scalar.Value, analysis);
                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence)
                {
                    CollectGameInfo(child, analysis);
                }

                break;
        }
    }

    private static void UpdatePlatformFlags(string? value, ModYamlAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "pc", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsPc = true;
        }
        else if (string.Equals(normalized, "ps2", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsPs2 = true;
        }
    }

    private static void UpdateGameFlags(string? value, ModYamlAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();

        if (string.Equals(normalized, "kh1", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsKh1 = true;
        }
        else if (string.Equals(normalized, "kh2", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsKh2 = true;
        }
        else if (string.Equals(normalized, "bbs", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsBbs = true;
        }
        else if (string.Equals(normalized, "recom", StringComparison.OrdinalIgnoreCase))
        {
            analysis.TargetsRecom = true;
        }
    }

    private static DateTime? ParseGitHubDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
        {
            return result;
        }

        if (DateTime.TryParse(value, out result))
        {
            return result;
        }

        return null;
    }

    private static bool HasCopyExtensionMismatch(string? targetName, YamlNode? sourceNode)
    {
        if (string.IsNullOrWhiteSpace(targetName) || sourceNode == null)
        {
            return false;
        }

        var targetExtension = Path.GetExtension(targetName);
        if (string.IsNullOrEmpty(targetExtension))
        {
            return false;
        }

        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSourceExtensions(sourceNode, sourceExtensions);

        if (sourceExtensions.Count == 0)
        {
            return false;
        }

        foreach (var extension in sourceExtensions)
        {
            if (!string.Equals(extension, targetExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectSourceExtensions(YamlNode node, ISet<string> extensions)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                AddExtensionIfPresent(scalar.Value, extensions);
                break;
            case YamlMappingNode mapping:
                foreach (var child in mapping.Children)
                {
                    var key = (child.Key as YamlScalarNode)?.Value;
                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "from", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = (child.Value as YamlScalarNode)?.Value;
                        AddExtensionIfPresent(value, extensions);
                    }

                    CollectSourceExtensions(child.Value, extensions);
                }

                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence)
                {
                    CollectSourceExtensions(child, extensions);
                }

                break;
        }
    }

    private static void AddExtensionIfPresent(string? value, ISet<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var extension = Path.GetExtension(value);
        if (!string.IsNullOrEmpty(extension))
        {
            extensions.Add(extension);
        }
    }

    private static bool ContainsRemasteredSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (!normalized.Contains('/') && !normalized.Contains('\\'))
        {
            return string.Equals(normalized, "remastered", StringComparison.OrdinalIgnoreCase);
        }

        var segments = normalized.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => string.Equals(segment, "remastered", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record BadgeQueryResult(IReadOnlyList<ModBadge> Badges, DateTime? CreatedAt, DateTime? LastPush);

    private sealed record RepositoryLanguageMetadata(
        bool HasLua,
        DateTime? CreatedAt,
        DateTime? LastPush,
        Dictionary<string, long>? Languages,
        string? Owner);

    private sealed class ModYamlAnalysis
    {
        public bool TargetsPc { get; set; }
        public bool TargetsPs2 { get; set; }
        public bool TargetsKh1 { get; set; }
        public bool TargetsKh2 { get; set; }
        public bool TargetsBbs { get; set; }
        public bool TargetsRecom { get; set; }
        public bool WritesRemasteredAssets { get; set; }
        public bool UsesHeavyCopy { get; set; }
        public bool HasCopyExtensionMismatch { get; set; }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenKh-ModBrowser/1.0 (+https://github.com/OpenKh/OpenKh)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string? ResolveModsFilePath()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EnumerateCandidateDirectories())
        {
            var candidate = Path.Combine(directory, "mods.json");
            if (!seen.Add(candidate))
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        foreach (var directory in EnumerateUpwards(AppContext.BaseDirectory))
        {
            yield return directory;
        }

        foreach (var directory in EnumerateUpwards(Directory.GetCurrentDirectory()))
        {
            yield return directory;
        }
    }

    private static IEnumerable<string> EnumerateUpwards(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            yield break;
        }

        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    private static string? ResolveFollowedUsersFilePath()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EnumerateCandidateDirectories())
        {
            var candidate = Path.Combine(directory, "followed-users.txt");
            if (!seen.Add(candidate))
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryNormalizeRepository(string? input, out string repository)
    {
        repository = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = input.Trim();

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            var isGitHubHost = string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(host, "www.github.com", StringComparison.OrdinalIgnoreCase);
            if (!isGitHubHost)
            {
                return false;
            }

            candidate = uri.AbsolutePath;
        }
        else if (candidate.Contains('@') && candidate.Contains(':'))
        {
            var colonIndex = candidate.IndexOf(':');
            if (colonIndex >= 0 && colonIndex + 1 < candidate.Length)
            {
                candidate = candidate[(colonIndex + 1)..];
            }
        }

        if (candidate.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["github.com/".Length..];
        }

        candidate = candidate.Trim('/');

        if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4];
        }

        var parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        repository = $"{parts[0]}/{parts[1]}";
        return true;
    }

    private static bool TryNormalizeUsername(string? input, out string username)
    {
        username = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = input.Trim();

        if (candidate.StartsWith("@", StringComparison.Ordinal))
        {
            candidate = candidate[1..];
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            var isGitHubHost = string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(host, "www.github.com", StringComparison.OrdinalIgnoreCase);
            if (!isGitHubHost)
            {
                return false;
            }

            candidate = uri.AbsolutePath;
        }

        if (candidate.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["github.com/".Length..];
        }

        candidate = candidate.Trim('/');
        if (candidate.Length == 0)
        {
            return false;
        }

        var slashIndex = candidate.IndexOf('/');
        if (slashIndex >= 0)
        {
            candidate = candidate[..slashIndex];
        }

        if (candidate.Length == 0)
        {
            return false;
        }

        username = candidate;
        return true;
    }

    private static ModCategory DetermineCategory(ModJsonEntry entry)
    {
        if (!HasModYml(entry))
        {
            return ModCategory.Other;
        }

        return HasIcon(entry) ? ModCategory.WithIcon : ModCategory.WithoutIcon;
    }

    private static bool HasModYml(ModJsonEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModYmlUrl))
        {
            return true;
        }

        if (entry.Matches != null &&
            entry.Matches.TryGetValue("mod.yml", out var modYmlMatch))
        {
            return modYmlMatch.Exists;
        }

        return false;
    }

    private static bool HasIcon(ModJsonEntry entry)
    {
        if (entry.HasIcon.HasValue)
        {
            return entry.HasIcon.Value;
        }

        if (entry.Matches != null &&
            entry.Matches.TryGetValue("icon.png", out var iconMatch))
        {
            return iconMatch.Exists;
        }

        return ExtractIconUrl(entry) != null;
    }

    private static string? ExtractIconUrl(ModJsonEntry entry)
    {
        if (entry.Matches != null && entry.Matches.TryGetValue("icon.png", out var iconMatch) && iconMatch.Exists)
        {
            if (!string.IsNullOrWhiteSpace(iconMatch.Url))
            {
                return iconMatch.Url;
            }
        }

        if (entry.HasIcon == true)
        {
            return BuildRawGitHubUrl(entry.Repo, "icon.png");
        }

        return null;
    }

    private static string? ExtractModYmlUrl(ModJsonEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModYmlUrl))
        {
            return entry.ModYmlUrl;
        }

        if (entry.Matches != null &&
            entry.Matches.TryGetValue("mod.yml", out var modYmlMatch) &&
            modYmlMatch.Exists &&
            !string.IsNullOrWhiteSpace(modYmlMatch.Url))
        {
            return modYmlMatch.Url;
        }

        return null;
    }

    private static string? BuildRawGitHubUrl(string? repo, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return $"https://raw.githubusercontent.com/{repo}/HEAD/{relativePath}";
    }

    private bool MatchesSearch(ModEntry mod)
    {
        if (!_activeFilters.MatchesAuthor(mod))
        {
            return false;
        }

        if (!_activeFilters.MatchesCreation(mod))
        {
            return false;
        }

        if (!_activeFilters.MatchesUpdate(mod))
        {
            return false;
        }

        if (!_activeFilters.MatchesName(mod))
        {
            return false;
        }

        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private record SearchFilters(
        List<string> NameTokens,
        string? Author,
        DateTime? CreatedFrom,
        DateTime? CreatedBefore,
        DateTime? UpdatedFrom)
    {
        public static SearchFilters Empty { get; } = new(new List<string>(), null, null, null, null);

        public static SearchFilters Parse(string query)
        {
            var nameTokens = new List<string>();
            string? author = null;
            DateTime? createdFrom = null;
            DateTime? createdBefore = null;
            DateTime? updatedFrom = null;

            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var token in Tokenize(query))
                {
                    var separatorIndex = token.IndexOf(':');
                    if (separatorIndex > 0)
                    {
                        var key = token[..separatorIndex].ToLowerInvariant();
                        var value = token[(separatorIndex + 1)..].Trim();
                        value = value.Trim('"');
                        value = value.Trim('{', '}');
                        switch (key)
                        {
                            case "from":
                                author = value;
                                break;
                            case "created":
                                createdFrom = ParseDate(value);
                                break;
                            case "before":
                                createdBefore = ParseDate(value);
                                break;
                            case "updated":
                                updatedFrom = ParseDate(value);
                                break;
                            default:
                                nameTokens.Add(token);
                                break;
                        }
                    }
                    else
                    {
                        nameTokens.Add(token);
                    }
                }
            }

            return new SearchFilters(nameTokens, author, createdFrom, createdBefore, updatedFrom);
        }

        private static IEnumerable<string> Tokenize(string query)
        {
            var buffer = new StringBuilder();
            var inQuotes = false;

            foreach (var ch in query)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (buffer.Length > 0)
                    {
                        yield return buffer.ToString();
                        buffer.Clear();
                    }

                    continue;
                }

                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
            }
        }

        public bool MatchesAuthor(ModEntry mod)
        {
            if (string.IsNullOrWhiteSpace(Author))
            {
                return true;
            }

            return string.Equals(mod.Author, Author, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesCreation(ModEntry mod)
        {
            if (CreatedFrom.HasValue)
            {
                if (!mod.CreatedAt.HasValue || mod.CreatedAt.Value < CreatedFrom.Value)
                {
                    return false;
                }
            }

            if (CreatedBefore.HasValue)
            {
                if (!mod.CreatedAt.HasValue || mod.CreatedAt.Value > CreatedBefore.Value)
                {
                    return false;
                }
            }

            return true;
        }

        public bool MatchesUpdate(ModEntry mod)
        {
            if (!UpdatedFrom.HasValue)
            {
                return true;
            }

            return mod.LastPush.HasValue && mod.LastPush.Value >= UpdatedFrom.Value;
        }

        public bool MatchesName(ModEntry mod)
        {
            if (NameTokens.Count == 0)
            {
                return true;
            }

            return NameTokens.All(token => MatchesToken(mod, token));
        }

        private static bool MatchesToken(ModEntry mod, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return true;
            }

            if (Contains(mod.Name, token) || Contains(mod.Repo, token))
            {
                return true;
            }

            var distance = LevenshteinDistance(mod.Name.ToLowerInvariant(), token.ToLowerInvariant());
            var threshold = Math.Max(2, token.Length / 2);
            return distance <= threshold;
        }

        private static bool Contains(string source, string value) =>
            CultureInfo.InvariantCulture.CompareInfo
                .IndexOf(source, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) >= 0;

        private static DateTime? ParseDate(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
                ? result
                : DateTime.TryParse(value, out result)
                    ? result
                    : null;

        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0)
            {
                return b.Length;
            }

            if (b.Length == 0)
            {
                return a.Length;
            }

            var distances = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++)
            {
                distances[j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                var previous = distances[0];
                distances[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var temp = distances[j];
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    distances[j] = Math.Min(
                        Math.Min(distances[j] + 1, distances[j - 1] + 1),
                        previous + cost);
                    previous = temp;
                }
            }

            return distances[b.Length];
        }
    }
}

public enum SortOption
{
    CreationDate,
    LastUpdate,
    AuthorName
}

public record SortOptionInfo(SortOption Option, string DisplayName);
