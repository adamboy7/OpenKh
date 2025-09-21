using OpenKh.Kh2;
using OpenKh.Kh2.Ard;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenKh.Command.SpawnPointExplorer;

internal sealed record SpawnDataSet(
    string RootPath,
    IReadOnlyList<MapSpawnData> Maps,
    IReadOnlyDictionary<uint, int> ObjectCounts,
    IReadOnlyList<SpawnScanIssue> Issues)
{
    public static SpawnDataSet Build(string rootPath)
    {
        var maps = new List<MapSpawnData>();
        var counts = new Dictionary<uint, int>();
        var issues = new List<SpawnScanIssue>();

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.ard", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var bar = Bar.Read(stream);
                var spawnEntries = new List<SpawnEntryData>();

                foreach (var entry in bar.Where(x => x.Type == Bar.EntryType.AreaDataSpawn && x.Stream.Length > 0))
                {
                    entry.Stream.Position = 0;
                    var spawnPoints = SpawnPoint.Read(entry.Stream);
                    spawnEntries.Add(new SpawnEntryData(entry.Name, spawnPoints));

                    foreach (var point in spawnPoints)
                    {
                        foreach (var entity in point.Entities)
                        {
                            var id = (uint)entity.ObjectId;
                            if (id == 0)
                            {
                                continue;
                            }

                            counts.TryGetValue(id, out var value);
                            counts[id] = value + 1;
                        }
                    }
                }

                foreach (var entry in bar)
                {
                    entry.Stream?.Dispose();
                }

                if (spawnEntries.Count > 0)
                {
                    var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
                    var mapName = Path.GetFileNameWithoutExtension(filePath);
                    maps.Add(new MapSpawnData(mapName, relativePath, spawnEntries));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new SpawnScanIssue(filePath, ex.Message));
            }
        }

        return new SpawnDataSet(rootPath, maps, counts, issues);
    }

    public List<EnemyCandidate> CreateCandidates(IReadOnlyDictionary<uint, Objentry> objEntries)
    {
        var list = new List<EnemyCandidate>();
        foreach (var (id, count) in ObjectCounts.OrderBy(pair => pair.Key))
        {
            objEntries.TryGetValue(id, out var objEntry);
            var modelName = objEntry?.ModelName ?? string.Empty;
            list.Add(new EnemyCandidate(id, modelName, objEntry?.ObjectType, count));
        }

        return list
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .ToList();
    }

    public List<MapEnemyOccurrences> FindEnemyOccurrences(uint objectId)
    {
        var result = new List<MapEnemyOccurrences>();
        foreach (var map in Maps)
        {
            var spawnGroups = new List<SpawnGroupOccurrences>();
            foreach (var spawnEntry in map.SpawnEntries)
            {
                var spawnPoints = new List<SpawnPointOccurrences>();
                foreach (var spawnPoint in spawnEntry.SpawnPoints)
                {
                    var matches = spawnPoint.Entities
                        .Where(entity => (uint)entity.ObjectId == objectId)
                        .ToList();

                    if (matches.Count > 0)
                    {
                        spawnPoints.Add(new SpawnPointOccurrences(spawnPoint, matches));
                    }
                }

                if (spawnPoints.Count > 0)
                {
                    spawnGroups.Add(new SpawnGroupOccurrences(spawnEntry.Name, spawnPoints));
                }
            }

            if (spawnGroups.Count > 0)
            {
                result.Add(new MapEnemyOccurrences(map.MapName, map.RelativePath, spawnGroups));
            }
        }

        return result;
    }
}

internal sealed record EnemyCandidate(uint Id, string ModelName, Objentry.Type? Type, int OccurrenceCount)
{
    public string CleanModelName => (ModelName ?? string.Empty).TrimEnd('\0').Trim();

    public string DisplayName => CleanModelName.Length == 0 ? $"0x{Id:X04}" : CleanModelName;
}

internal sealed record MapSpawnData(string MapName, string RelativePath, IReadOnlyList<SpawnEntryData> SpawnEntries);

internal sealed record SpawnEntryData(string Name, IReadOnlyList<SpawnPoint> SpawnPoints);

internal sealed record MapEnemyOccurrences(string MapName, string RelativePath, IReadOnlyList<SpawnGroupOccurrences> SpawnGroups);

internal sealed record SpawnGroupOccurrences(string SpawnName, IReadOnlyList<SpawnPointOccurrences> SpawnPoints);

internal sealed record SpawnPointOccurrences(SpawnPoint Spawn, IReadOnlyList<SpawnPoint.Entity> Entities);

internal sealed record SpawnScanIssue(string FilePath, string Message);
