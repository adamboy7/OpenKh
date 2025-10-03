using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenKh.Tools.ModBrowser.Models;

public class ModJsonMatch
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class ModJsonEntry
{
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("last_push")]
    public DateTime? LastPush { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("matches")]
    public Dictionary<string, ModJsonMatch>? Matches { get; set; }

    [JsonPropertyName("mod_yml_url")]
    public string? ModYmlUrl { get; set; }

    [JsonPropertyName("has_icon")]
    public bool? HasIcon { get; set; }

    [JsonPropertyName("languages")]
    public Dictionary<string, long>? Languages { get; set; }

    [JsonPropertyName("badges")]
    public List<ModJsonBadge>? Badges { get; set; }
}

public class ModJsonBadge
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("foreground")]
    public string? Foreground { get; set; }
}
