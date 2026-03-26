using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Services
{
    public class LoggedCallEntry
    {
        public string UniqueId { get; set; } = "";
        public string OperatorId { get; set; } = "";
        public DateTime LoggedAt { get; set; }
    }

    public class LoggedCallService
    {
        private static readonly string FilePath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OrbitalSIP", "logged-calls.json");

        private List<LoggedCallEntry> _entries = new();
        private readonly object _lock = new();

        public LoggedCallService()
        {
            Load();
            Cleanup();
        }

        public void MarkCallAsLogged(string uniqueId, string operatorId)
        {
            if (string.IsNullOrEmpty(uniqueId) || string.IsNullOrEmpty(operatorId)) return;

            lock (_lock)
            {
                var existing = _entries.FirstOrDefault(e => e.UniqueId == uniqueId && e.OperatorId == operatorId);
                if (existing != null)
                {
                    existing.LoggedAt = DateTime.UtcNow;
                }
                else
                {
                    _entries.Add(new LoggedCallEntry
                    {
                        UniqueId = uniqueId,
                        OperatorId = operatorId,
                        LoggedAt = DateTime.UtcNow
                    });
                }
                Save();
            }
        }

        public bool IsCallLogged(string uniqueId, string operatorId)
        {
            if (string.IsNullOrEmpty(uniqueId) || string.IsNullOrEmpty(operatorId)) return false;

            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e => e.UniqueId == uniqueId && e.OperatorId == operatorId);
                if (entry != null)
                {
                    if (entry.LoggedAt >= DateTime.UtcNow.AddHours(-24))
                    {
                        return true;
                    }
                    else
                    {
                        // Stale entry, cleanup
                        _entries.Remove(entry);
                        Save();
                    }
                }
                return false;
            }
        }

        private void Cleanup()
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddHours(-24);
                int removed = _entries.RemoveAll(e => e.LoggedAt < cutoff);
                if (removed > 0)
                {
                    Save();
                }
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<List<LoggedCallEntry>>(json);
                    if (loaded != null)
                    {
                        _entries = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LoggedCallService", $"Error loading logged calls: {ex.Message}");
                _entries = new List<LoggedCallEntry>();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Log("LoggedCallService", $"Error saving logged calls: {ex.Message}");
            }
        }
    }
}
