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
    private DateTime? _createdAt;
    private DateTime? _lastPush;
    private string? _iconPath;

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
        _createdAt = createdAt;
        _lastPush = lastPush;
        RemoteIconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl;
        IconUrl = RemoteIconUrl;
        Category = category;
        ModYmlUrl = string.IsNullOrWhiteSpace(modYmlUrl) ? null : modYmlUrl;
    }

    public string Repo { get; }

    public string Name => Repo.Contains('/') ? Repo[(Repo.IndexOf('/') + 1)..] : Repo;

    public string? Author { get; }

    public DateTime? CreatedAt
    {
        get => _createdAt;
        private set
        {
            if (Nullable.Equals(_createdAt, value))
            {
                return;
            }

            _createdAt = value;
            OnPropertyChanged(nameof(CreatedAt));
            OnPropertyChanged(nameof(CreatedAtDisplay));
        }
    }

    public DateTime? LastPush
    {
        get => _lastPush;
        private set
        {
            if (Nullable.Equals(_lastPush, value))
            {
                return;
            }

            _lastPush = value;
            OnPropertyChanged(nameof(LastPush));
            OnPropertyChanged(nameof(LastUpdatedDisplay));
        }
    }

    public string? RemoteIconUrl { get; }

    public string? IconUrl
    {
        get => _iconPath;
        private set
        {
            if (_iconPath == value)
            {
                return;
            }

            _iconPath = value;
            OnPropertyChanged(nameof(IconUrl));
            OnPropertyChanged(nameof(HasIcon));
        }
    }

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

    public string CreatedAtDisplay => FormatDate("Created", CreatedAt);

    public string LastUpdatedDisplay => FormatDate("Updated", LastPush);

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool UpdateDates(DateTime? createdAt, DateTime? lastPush)
    {
        var changed = false;

        if (!Nullable.Equals(CreatedAt, createdAt))
        {
            CreatedAt = createdAt;
            changed = true;
        }

        if (!Nullable.Equals(LastPush, lastPush))
        {
            LastPush = lastPush;
            changed = true;
        }

        return changed;
    }

    public void UpdateBadges(IReadOnlyList<ModBadge>? badges) => Badges = badges ?? Array.Empty<ModBadge>();

    public void SetLoadingBadges(bool isLoading) => IsLoadingBadges = isLoading;

    public void SetIconPath(string? iconPath) => IconUrl = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;

    private static string FormatDate(string prefix, DateTime? value) => value.HasValue
        ? $"{prefix}: {value.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : $"{prefix}: Unknown";

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
