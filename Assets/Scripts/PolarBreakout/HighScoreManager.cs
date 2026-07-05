using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Persists the top named scores ever achieved, static and PlayerPrefs-backed like
    /// GameSettings - no MonoBehaviour/singleton needed since it's just data with no per-frame
    /// behavior.
    /// </summary>
    public static class HighScoreManager
    {
        private const string CountKey = "HighScores.Count";
        private const string NameKeyPrefix = "HighScores.Name.";
        private const string ScoreKeyPrefix = "HighScores.Score.";
        private const int MaxEntries = 10;

        public struct Entry
        {
            public string Name;
            public int Score;
        }

        /// <summary>Top entries, highest score first. Never longer than MaxEntries.</summary>
        public static List<Entry> Load()
        {
            int count = Mathf.Clamp(PlayerPrefs.GetInt(CountKey, 0), 0, MaxEntries);
            var entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                entries.Add(new Entry
                {
                    Name = PlayerPrefs.GetString(NameKeyPrefix + i, "---"),
                    Score = PlayerPrefs.GetInt(ScoreKeyPrefix + i, 0),
                });
            }
            return entries;
        }

        /// <summary>True if a run ending with this score would actually make it onto the table -
        /// checked before prompting for a name, so a non-qualifying run skips name entry.</summary>
        public static bool QualifiesForTable(int score)
        {
            var entries = Load();
            return entries.Count < MaxEntries || score > entries[entries.Count - 1].Score;
        }

        /// <summary>Inserts a run's name+score into the table (re-sorted by score descending,
        /// capped at MaxEntries) and persists it immediately. Returns the entry's resulting
        /// index (0 = top rank) so the caller can highlight it - always >= 0 as long as
        /// QualifiesForTable(score) was true beforehand.</summary>
        public static int SubmitEntry(string name, int score)
        {
            var entries = Load();
            entries.Add(new Entry { Name = name, Score = score });
            entries.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

            PlayerPrefs.SetInt(CountKey, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPrefs.SetString(NameKeyPrefix + i, entries[i].Name);
                PlayerPrefs.SetInt(ScoreKeyPrefix + i, entries[i].Score);
            }
            PlayerPrefs.Save();

            return entries.FindIndex(e => e.Name == name && e.Score == score);
        }

        /// <summary>The single highest score ever recorded, or 0 if none yet.</summary>
        public static int GetTopScore()
        {
            var entries = Load();
            return entries.Count > 0 ? entries[0].Score : 0;
        }

        /// <summary>Wipes the whole table. Old per-index keys beyond the new (zero) count are
        /// simply orphaned - Load() only ever reads up to the stored count, so stale leftover
        /// keys are harmless.</summary>
        public static void ClearAll()
        {
            PlayerPrefs.SetInt(CountKey, 0);
            PlayerPrefs.Save();
        }
    }
}
