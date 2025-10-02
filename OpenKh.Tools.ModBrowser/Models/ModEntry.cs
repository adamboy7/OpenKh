using System;
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
        bool hasLua)
    {
        Repo = repo;
        Author = author;
        CreatedAt = createdAt;
        LastPush = lastPush;
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl;
        Category = category;
        _hasLua = hasLua;
    }

    public string Repo { get; }

    public string Name => Repo.Contains('/') ? Repo[(Repo.IndexOf('/') + 1)..] : Repo;

    public string? Author { get; }

    public DateTime? CreatedAt { get; }

    public DateTime? LastPush { get; }

    public string? IconUrl { get; }

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    public ModCategory Category { get; }

    private bool _hasLua;

    public bool HasLua
    {
        get => _hasLua;
        private set
        {
            if (_hasLua == value)
            {
                return;
            }

            _hasLua = value;
            OnPropertyChanged(nameof(HasLua));
        }
    }

    private bool _isCheckingLanguages;

    public bool IsCheckingLanguages
    {
        get => _isCheckingLanguages;
        private set
        {
            if (_isCheckingLanguages == value)
            {
                return;
            }

            _isCheckingLanguages = value;
            OnPropertyChanged(nameof(IsCheckingLanguages));
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

    public void UpdateLuaUsage(bool hasLua) => HasLua = hasLua;

    public void SetCheckingLanguages(bool isChecking) => IsCheckingLanguages = isChecking;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
