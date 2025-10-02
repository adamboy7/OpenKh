using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using OpenKh.Tools.ModBrowser.Models;

namespace OpenKh.Tools.ModBrowser.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<ModEntry> _mods = new();
    private string _searchQuery = string.Empty;
    private SortOption _selectedSortOption;
    private SearchFilters _activeFilters = SearchFilters.Empty;

    public MainViewModel()
    {
        ModsWithIconView = CreateView(ModCategory.WithIcon);
        ModsWithoutIconView = CreateView(ModCategory.WithoutIcon);
        ModsOtherView = CreateView(ModCategory.Other);

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
            foreach (var entry in entries)
            {
                var category = DetermineCategory(entry);
                var iconUrl = ExtractIconUrl(entry);
                var hasLua = HasLuaLanguage(entry);
                _mods.Add(new ModEntry(
                    entry.Repo,
                    entry.Author,
                    entry.CreatedAt,
                    entry.LastPush,
                    iconUrl,
                    category,
                    hasLua));
            }

            RefreshViews();
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException)
        {
            // Swallow and keep list empty. In a real app we might log this.
        }
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
        if (entry.HasIcon.HasValue)
        {
            return entry.HasIcon.Value ? ModCategory.WithIcon : ModCategory.WithoutIcon;
        }

        return ModCategory.Other;
    }

    private static string? ExtractIconUrl(ModJsonEntry entry)
    {
        if (entry.Matches != null && entry.Matches.TryGetValue("icon.png", out var iconMatch) && iconMatch.Exists)
        {
            return string.IsNullOrWhiteSpace(iconMatch.Url) ? null : iconMatch.Url;
        }

        return null;
    }

    private static bool HasLuaLanguage(ModJsonEntry entry)
    {
        if (entry.Languages == null || entry.Languages.Count == 0)
        {
            return false;
        }

        foreach (var language in entry.Languages.Keys)
        {
            if (string.Equals(language, "Lua", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
