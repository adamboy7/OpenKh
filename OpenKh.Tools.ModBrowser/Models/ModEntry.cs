using System;
using System.Globalization;
namespace OpenKh.Tools.ModBrowser.Models;

public enum ModCategory
{
    WithIcon,
    WithoutIcon,
    Other
}

public class ModEntry
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
        HasLua = hasLua;
    }

    public string Repo { get; }

    public string Name => Repo.Contains('/') ? Repo[(Repo.IndexOf('/') + 1)..] : Repo;

    public string? Author { get; }

    public DateTime? CreatedAt { get; }

    public DateTime? LastPush { get; }

    public string? IconUrl { get; }

    public ModCategory Category { get; }

    public bool HasLua { get; }

    public string DisplayAuthor => string.IsNullOrWhiteSpace(Author) ? "Unknown author" : Author!;

    public string CreatedAtDisplay => CreatedAt.HasValue
        ? $"Created: {CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : "Created: Unknown";

    public string LastUpdatedDisplay => LastPush.HasValue
        ? $"Updated: {LastPush.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : "Updated: Unknown";
}
