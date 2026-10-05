using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4Companion.Crafting
{
    /// <summary>
    /// One saved version of an item, for example the first scan or the result after a craft.
    /// </summary>
    public sealed record GearRevision
    {
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public string Label { get; init; } = string.Empty;
        public GearSnapshot Snapshot { get; init; } = new();
    }

    /// <summary>
    /// A saved item with its full crafting history, oldest revision first.
    /// </summary>
    public sealed record GearRecord
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string ItemType { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public IReadOnlyList<GearRevision> Revisions { get; init; } = Array.Empty<GearRevision>();

        [JsonIgnore]
        public GearRevision? Latest => Revisions.Count > 0 ? Revisions[^1] : null;
    }

    public sealed record GearStoreResult(bool Success, string Message, GearRecord? Record = null, ValidationResult? Validation = null);

    /// <summary>
    /// Saves items and their crafting history to a JSON file.
    /// A damaged file is set aside (renamed) instead of crashing the app or being overwritten.
    /// </summary>
    public sealed class GearStore
    {
        public const int SchemaVersion = 1;

        private readonly object _lock = new();
        private readonly string _filePath;
        private List<GearRecord> _records = new();

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public GearStore(string filePath)
        {
            _filePath = filePath;
        }

        public string FilePath => _filePath;

        public IReadOnlyList<GearRecord> Records
        {
            get { lock (_lock) return _records.ToList(); }
        }

        /// <summary>
        /// Messages about anything skipped or recovered during the last load.
        /// </summary>
        public IReadOnlyList<string> LoadIssues { get; private set; } = Array.Empty<string>();

        public void Load()
        {
            lock (_lock)
            {
                var issues = new List<string>();
                _records = new List<GearRecord>();

                if (!File.Exists(_filePath))
                {
                    LoadIssues = issues;
                    return;
                }

                StoreFile? file = null;
                try
                {
                    string json = File.ReadAllText(_filePath);
                    file = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
                {
                    file = null;
                }

                if (file?.Items == null)
                {
                    string backup = SetAsideCorruptFile();
                    issues.Add($"Saved gear could not be read. The damaged file was kept as {Path.GetFileName(backup)} and a new list was started.");
                    LoadIssues = issues;
                    return;
                }

                foreach (var record in file.Items)
                {
                    if (record == null) continue;
                    var revisions = new List<GearRevision>();
                    foreach (var revision in record.Revisions ?? Array.Empty<GearRevision>())
                    {
                        if (revision?.Snapshot == null) continue;
                        var clean = GearValidator.Sanitize(revision.Snapshot);
                        if (!GearValidator.Validate(clean).CanSave) continue;
                        revisions.Add(revision with { Snapshot = clean, Label = revision.Label ?? string.Empty });
                    }

                    int skipped = (record.Revisions?.Count ?? 0) - revisions.Count;
                    if (skipped > 0)
                    {
                        issues.Add($"Skipped {skipped} unreadable entr{(skipped == 1 ? "y" : "ies")} in saved gear.");
                    }
                    if (revisions.Count == 0) continue;

                    _records.Add(record with
                    {
                        Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                        ItemType = revisions[^1].Snapshot.ItemType,
                        Revisions = revisions
                    });
                }

                LoadIssues = issues;
            }
        }

        /// <summary>
        /// Saves a new item. The snapshot is cleaned and validated first.
        /// </summary>
        public GearStoreResult AddItem(GearSnapshot snapshot, string label = "Scanned")
        {
            var clean = GearValidator.Sanitize(snapshot);
            var validation = GearValidator.Validate(clean);
            if (!validation.CanSave)
            {
                return new GearStoreResult(false, "The item has invalid data and was not saved.", null, validation);
            }

            lock (_lock)
            {
                var record = new GearRecord
                {
                    ItemType = clean.ItemType,
                    Revisions = new List<GearRevision> { new() { Label = label, Snapshot = clean } }
                };
                _records.Add(record);
                Persist();
                return new GearStoreResult(true, "Item saved.", record, validation);
            }
        }

        /// <summary>
        /// Adds a new version of an existing item, for example after crafting. Earlier versions are kept.
        /// </summary>
        public GearStoreResult AddRevision(Guid recordId, GearSnapshot snapshot, string label = "After crafting")
        {
            var clean = GearValidator.Sanitize(snapshot);
            var validation = GearValidator.Validate(clean);
            if (!validation.CanSave)
            {
                return new GearStoreResult(false, "The item has invalid data and was not saved.", null, validation);
            }

            lock (_lock)
            {
                int index = _records.FindIndex(r => r.Id == recordId);
                if (index < 0)
                {
                    return new GearStoreResult(false, "The saved item no longer exists.", null, validation);
                }

                var record = _records[index];
                if (!string.Equals(record.ItemType, clean.ItemType, StringComparison.OrdinalIgnoreCase))
                {
                    return new GearStoreResult(false, $"The new scan is a {clean.ItemType}, but the saved item is a {record.ItemType}.", null, validation);
                }

                var updated = record with
                {
                    Revisions = record.Revisions.Append(new GearRevision { Label = label, Snapshot = clean }).ToList()
                };
                _records[index] = updated;
                Persist();
                return new GearStoreResult(true, "New version saved.", updated, validation);
            }
        }

        public bool Delete(Guid recordId)
        {
            lock (_lock)
            {
                bool removed = _records.RemoveAll(r => r.Id == recordId) > 0;
                if (removed) Persist();
                return removed;
            }
        }

        public GearRecord? Find(Guid recordId)
        {
            lock (_lock) return _records.FirstOrDefault(r => r.Id == recordId);
        }

        private void Persist()
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var file = new StoreFile { Version = SchemaVersion, Items = _records.ToList() };
            string json = JsonSerializer.Serialize(file, JsonOptions);

            // Write to a temporary file first so a crash cannot leave a half-written store.
            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }

        private string SetAsideCorruptFile()
        {
            string backup = Path.Combine(
                Path.GetDirectoryName(_filePath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(_filePath)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(_filePath)}");
            try
            {
                File.Move(_filePath, backup, overwrite: true);
            }
            catch (IOException)
            {
                // Leave the file in place; it will be replaced on the next save.
            }
            return backup;
        }

        private sealed class StoreFile
        {
            public int Version { get; set; }
            public List<GearRecord>? Items { get; set; }
        }
    }
}
