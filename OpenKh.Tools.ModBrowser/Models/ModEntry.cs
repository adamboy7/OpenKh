using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace OpenKh.Tools.ModBrowser.Models;

public enum ModCategory
{
    WithIcon,
    WithoutIcon,
    Other
}

public class ModEntry : INotifyPropertyChanged
{
    public ModEntry(
        string repo,
        string? author,
        DateTime? createdAt,
        DateTime? lastPush,
        string? iconUrl,
        ModCategory category,
        string? modYmlUrl)
    {
        Repo = repo;
        Author = author;
        CreatedAt = createdAt;
        LastPush = lastPush;
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl;
        Category = category;
        ModYmlUrl = string.IsNullOrWhiteSpace(modYmlUrl) ? null : modYmlUrl;
    }

    public string Repo { get; }

    public string Name => Repo.Contains('/') ? Repo[(Repo.IndexOf('/') + 1)..] : Repo;

    public string? Author { get; }

    public DateTime? CreatedAt { get; }

    public DateTime? LastPush { get; }

    public string? IconUrl { get; }

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    public ModCategory Category { get; }

    public string? ModYmlUrl { get; }

    private IReadOnlyList<ModBadge> _badges = Array.Empty<ModBadge>();

    public IReadOnlyList<ModBadge> Badges
    {
        get => _badges;
        private set
        {
            if (ReferenceEquals(_badges, value))
            {
                return;
            }

            _badges = value;
            OnPropertyChanged(nameof(Badges));
            OnPropertyChanged(nameof(HasBadges));
        }
    }

    public bool HasBadges => Badges.Count > 0;

    private bool _isLoadingBadges;

    public bool IsLoadingBadges
    {
        get => _isLoadingBadges;
        private set
        {
            if (_isLoadingBadges == value)
            {
                return;
            }

            _isLoadingBadges = value;
            OnPropertyChanged(nameof(IsLoadingBadges));
        }
    }

    public string DisplayAuthor => string.IsNullOrWhiteSpace(Author) ? "Unknown author" : Author!;

    public string CreatedAtDisplay => CreatedAt.HasValue
        ? $"Created: {CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : "Created: Unknown";

    public string LastUpdatedDisplay => LastPush.HasValue
        ? $"Updated: {LastPush.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : "Updated: Unknown";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateBadges(IReadOnlyList<ModBadge>? badges) => Badges = badges ?? Array.Empty<ModBadge>();

    public void SetLoadingBadges(bool isLoading) => IsLoadingBadges = isLoading;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
