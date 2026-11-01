using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grechka.Quests
{
    // A draft is not an active quest pack. Incomplete editor values must survive restart.
    public sealed class DraftDocument
    {
        public int version = 1;
        public QuestPack pack;
        public Dictionary<string, string> numberBuffers = new Dictionary<string, string>();
    }

    public sealed class DraftStore
    {
        public const int MaxDraftBytes = 4 * 1024 * 1024;
        const int BackupsToKeep = 10;
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            IncludeFields = true, WriteIndented = true, MaxDepth = 32,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        readonly string root;
        public DraftStore(string root) { this.root = root ?? throw new ArgumentNullException(nameof(root)); }

        // Legacy saves do not contain a mod-specific colony ID. Derive a stable owner
        // from persisted world identity so a crash before the next save does not orphan drafts.
        public static string OwnerForWorld(string seed, int persistentRandomValue)
        {
            if (string.IsNullOrEmpty(seed)) throw new FormatException("У мира нет сохранённого идентификатора для черновика.");
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("grechka-draft-v1\n" + seed + "\n" + persistentRandomValue));
            var bytes = new byte[16];
            Array.Copy(hash, bytes, bytes.Length);
            return new Guid(bytes).ToString("N");
        }

        static void CheckOwner(string owner)
        {
            Guid id;
            if (!Guid.TryParseExact(owner, "N", out id)) throw new FormatException("Некорректный ID колонии для черновика.");
        }
        static string BookName(string id)
        {
            if (!PackCodec.ValidId(id)) throw new FormatException("Некорректный ID книги для черновика.");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        }
        string DraftFolder(string owner) { CheckOwner(owner); return Path.Combine(root, "Drafts", owner); }
        string BackupFolder(string owner, string name) { CheckOwner(owner); return Path.Combine(root, "Backups", owner, name); }
        string DraftPath(string owner, string id) { return Path.Combine(DraftFolder(owner), BookName(id) + ".json"); }

        public static string Encode(DraftDocument draft)
        {
            CheckStructure(draft);
            string json = JsonSerializer.Serialize(draft, Options);
            if (Encoding.UTF8.GetByteCount(json) > MaxDraftBytes) throw new FormatException("Черновик превышает 4 МБ.");
            return json;
        }

        public static DraftDocument Decode(string json)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > MaxDraftBytes) throw new FormatException("Черновик превышает 4 МБ или пуст.");
            var draft = JsonSerializer.Deserialize<DraftDocument>(json, Options);
            CheckStructure(draft);
            return draft;
        }

        static void CheckStructure(DraftDocument d)
        {
            if (d == null || d.version != 1 || d.pack == null || d.pack.schemaVersion != 1 || !PackCodec.ValidId(d.pack.id))
                throw new FormatException("Неизвестная версия или повреждённый черновик.");
            if (d.numberBuffers == null || d.numberBuffers.Count > 20000 || d.pack.chapters == null || d.pack.quests == null
                || d.pack.chapters.Count > 100 || d.pack.quests.Count > 500)
                throw new FormatException("Повреждена структура черновика.");
            if (d.pack.chapters.Any(c => c == null || c.id == null || c.title == null)
                || d.pack.quests.Any(q => q == null || q.id == null || q.chapter == null || q.title == null || q.description == null
                    || q.requires == null || q.objectives == null || q.rewards == null || q.alternatives == null || q.requires.Any(x => x == null)
                    || q.objectives.Any(o => o == null || o.id == null || o.type == null || o.target == null || o.text == null)
                    || q.rewards.Any(x => x == null || x.def == null || x.stuff == null)
                    || q.alternatives.Any(a => a == null || a.id == null || a.title == null || a.items == null
                        || a.items.Any(x => x == null || x.def == null || x.stuff == null))))
                throw new FormatException("Неполная структура черновика.");
            if (d.numberBuffers.Any(x => x.Key == null || x.Value == null || x.Key.Length > 256 || x.Value.Length > 64))
                throw new FormatException("Повреждены числовые поля черновика.");
        }

        static string ReadBounded(string path)
        {
            if (new FileInfo(path).Length > MaxDraftBytes) throw new FormatException("Черновик превышает 4 МБ.");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public DraftDocument LoadLatest(string owner, out string path)
        {
            string folder = DraftFolder(owner);
            path = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
            if (path == null) return null;
            var draft = Decode(ReadBounded(path));
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), BookName(draft.pack.id), StringComparison.Ordinal))
                throw new FormatException("ID книги в черновике не соответствует имени файла.");
            return draft;
        }

        // Refuse to overwrite corrupt/unknown existing data, even if a new draft happens to use the same ID.
        public void Save(string owner, DraftDocument document)
        {
            string json = Encode(document);
            string path = DraftPath(owner, document.pack.id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string previous = null;
            if (File.Exists(path))
            {
                previous = ReadBounded(path);
                var existing = Decode(previous);
                if (existing.pack.id != document.pack.id) throw new FormatException("ID сохранённого черновика не совпадает.");
                if (previous == json) return;
                string backupFolder = BackupFolder(owner, BookName(document.pack.id));
                Directory.CreateDirectory(backupFolder);
                string backup = Path.Combine(backupFolder, DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                File.Copy(path, backup, false);
            }
            AtomicWrite(path, json);
            if (previous != null) TrimBackups(owner, document.pack.id);
        }

        void TrimBackups(string owner, string book)
        {
            string folder = BackupFolder(owner, BookName(book));
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.GetFiles(folder, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(BackupsToKeep))
                File.Delete(path);
        }

        static void AtomicWrite(string path, string data)
        {
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(data); writer.Flush(); stream.Flush(true);
                }
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        string CheckDamagedPath(string owner, string damagedPath)
        {
            string folder = DraftFolder(owner);
            if (string.IsNullOrEmpty(damagedPath) || Path.GetDirectoryName(Path.GetFullPath(damagedPath)) != Path.GetFullPath(folder))
                throw new FormatException("Не удалось определить повреждённый черновик этой колонии.");
            string name = Path.GetFileNameWithoutExtension(damagedPath);
            if (name.Length != 64 || name.Any(c => !Uri.IsHexDigit(c)) || !string.Equals(Path.GetExtension(damagedPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Некорректное имя черновика.");
            return name;
        }

        // Invoked only after an explicit request to start over. Retain the unreadable original.
        public void ArchiveDamagedAndStart(string owner, string damagedPath, DraftDocument replacement)
        {
            string name = CheckDamagedPath(owner, damagedPath);
            string json = Encode(replacement);
            string target = DraftPath(owner, replacement.pack.id);
            if (!File.Exists(damagedPath)) throw new FileNotFoundException("Повреждённый черновик больше не найден.");
            if (string.Equals(BookName(replacement.pack.id), name, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(damagedPath, damagedPath + ".damaged-" + Guid.NewGuid().ToString("N"), false);
                AtomicWrite(target, json);
            }
            else
            {
                Save(owner, replacement);
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                File.Move(damagedPath, damagedPath + ".damaged-" + Guid.NewGuid().ToString("N"));
            }
        }

        // Explicit recovery preserves the damaged original and never silently swaps a different book.
        public DraftDocument RecoverLatest(string owner, string damagedPath)
        {
            string name = CheckDamagedPath(owner, damagedPath);
            string backups = BackupFolder(owner, name);
            if (!Directory.Exists(backups)) throw new FileNotFoundException("Резервные копии черновика не найдены.");
            foreach (string path in Directory.GetFiles(backups, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
            {
                DraftDocument recovered;
                string json;
                try { json = ReadBounded(path); recovered = Decode(json); }
                catch (Exception e) when (e is IOException || e is FormatException || e is JsonException) { continue; }
                if (!string.Equals(BookName(recovered.pack.id), name, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(damagedPath)) File.Copy(damagedPath, damagedPath + ".damaged-" + Guid.NewGuid().ToString("N"), false);
                AtomicWrite(damagedPath, json);
                return recovered;
            }
            throw new FormatException("Исправная резервная копия этой книги не найдена.");
        }
    }
}
