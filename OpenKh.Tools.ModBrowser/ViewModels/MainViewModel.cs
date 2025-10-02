using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using OpenKh.Tools.Common.Wpf;
using OpenKh.Tools.ModBrowser.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenKh.Tools.ModBrowser.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<ModEntry> _mods = new();
    private readonly Dictionary<string, IReadOnlyList<ModBadge>> _badgeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient = CreateHttpClient();
    private string _searchQuery = string.Empty;
    private SortOption _selectedSortOption;
    private SearchFilters _activeFilters = SearchFilters.Empty;

    public MainViewModel()
    {
        ModsWithIconView = CreateView(ModCategory.WithIcon);
        ModsWithoutIconView = CreateView(ModCategory.WithoutIcon);
        ModsOtherView = CreateView(ModCategory.Other);

        LoadBadgesCommand = new RelayCommand<ModEntry>(entry => _ = LoadBadgesForEntryAsync(entry));

        SortOptions = new List<SortOptionInfo>
        {
            new(SortOption.CreationDate, "Creation Date (Newest)"),
            new(SortOption.LastUpdate, "Last Update (Newest)"),
            new(SortOption.AuthorName, "Author Name (A-Z)")
        };

        _selectedSortOption = SortOption.CreationDate;
        ApplySorting();
        LoadMods();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView ModsWithIconView { get; }

    public ICollectionView ModsWithoutIconView { get; }

    public ICollectionView ModsOtherView { get; }

    public RelayCommand<ModEntry> LoadBadgesCommand { get; }

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
            foreach (var entry in entries)
            {
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
            var badges = await QueryBadgesAsync(entry, cancellationToken).ConfigureAwait(false);
            _badgeCache[entry.Repo] = badges;
            entry.UpdateBadges(badges);
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

    private async Task<IReadOnlyList<ModBadge>> QueryBadgesAsync(ModEntry entry, CancellationToken cancellationToken)
    {
        var badges = new List<ModBadge>();

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

            if (analysis.WritesRemasteredAssets)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateHd());
            }

            if (analysis.UsesHeavyCopy)
            {
                AddBadgeIfMissing(badges, ModBadge.CreateHeavy());
            }
        }

        return badges;
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

                    if (child.Value is YamlScalarNode scalarValue && ContainsRemasteredSegment(scalarValue.Value))
                    {
                        analysis.WritesRemasteredAssets = true;
                    }

                    if (string.Equals(key, "platform", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "platforms", StringComparison.OrdinalIgnoreCase))
                    {
                        CollectPlatformInfo(child.Value, analysis);
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

    private sealed class ModYamlAnalysis
    {
        public bool TargetsPc { get; set; }
        public bool TargetsPs2 { get; set; }
        public bool WritesRemasteredAssets { get; set; }
        public bool UsesHeavyCopy { get; set; }
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
