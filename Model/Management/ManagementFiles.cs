using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Dapper;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Management
{
    internal sealed partial class StudyStore
    {
        public List<Word> PreviewEnglish(byte[] bytes, string filename)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) throw new ArgumentException("Excel 文件应小于 10MB。");
            string extension = Path.GetExtension(filename ?? "").ToLowerInvariant();
            if (extension != ".xlsx" && extension != ".xls") throw new ArgumentException("请选择 xlsx 或 xls 文件。");
            if (extension == ".xlsx")
                using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                    if (zip.Entries.Sum(e => e.Length) > 50L * 1024 * 1024) throw new ArgumentException("Excel 解压内容过大。");
            using (var stream = new MemoryStream(bytes))
            {
                var workbook = WorkbookFactory.Create(stream);
                try
                {
                    if (workbook.NumberOfSheets == 0) throw new ArgumentException("Excel 中没有工作表。");
                    var sheet = workbook.GetSheetAt(0);
                    if (sheet.GetRow(0)?.GetCell(0)?.ToString() != "英语" || sheet.GetRow(0)?.GetCell(1)?.ToString() != "单词" || sheet.GetRow(0)?.GetCell(2)?.ToString() != "翻译")
                        throw new ArgumentException("请使用 ToastFish 英语导出模板，首行应以“英语、单词、翻译”开头。");
                    if (sheet.LastRowNum > 20000) throw new ArgumentException("一次最多导入 20000 行。");
                    var result = new List<Word>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i <= sheet.LastRowNum; i++)
                    {
                        var row = sheet.GetRow(i);
                        if (row == null || row.Cells.All(c => string.IsNullOrWhiteSpace(c.ToString()))) continue;
                        Func<int, string> cell = index => row.GetCell(index)?.ToString().Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(cell(1)) || string.IsNullOrWhiteSpace(cell(2))) throw new ArgumentException("第 " + (i + 1) + " 行缺少单词或翻译。");
                        if (row.Cells.Any(c => c.ToString().Length > 4000)) throw new ArgumentException("第 " + (i + 1) + " 行存在过长内容。");
                        if (!seen.Add(cell(1))) throw new ArgumentException("第 " + (i + 1) + " 行的单词重复：" + cell(1));
                        result.Add(new Word { wordRank = result.Count + 1, headWord = cell(1), tranCN = cell(2), ukPhone = cell(3),
                            usPhone = cell(4), pos = cell(5), phrase = cell(6), phraseCN = cell(7), sentence = cell(8), sentenceCN = cell(9),
                            question = cell(10), explain = cell(11), choiceIndexOne = cell(12), choiceIndexTwo = cell(13),
                            choiceIndexThree = cell(14), choiceIndexFour = cell(15), rightIndex = cell(16) });
                    }
                    if (result.Count == 0) throw new ArgumentException("文件中没有英语词条。");
                    return result;
                }
                finally { workbook.Close(); }
            }
        }

        public string ImportEnglish(string name, byte[] bytes, string filename)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) throw new ArgumentException("词库名称应为 1～80 个字符。");
            var words = PreviewEnglish(bytes, filename);
            string id = "personal_" + Guid.NewGuid().ToString("N");
            lock (Gate)
            {
                if (Pending() != null) throw new InvalidOperationException("请先结束未完成的学习，再导入词库。");
                using (var db = Open()) using (var tx = db.BeginTransaction())
                {
                    db.Execute("INSERT INTO ManagedBooks(Id,Name,Personal) VALUES(@id,@name,1)", new { id, name = name.Trim() }, tx);
                    foreach (var word in words) InsertWord(db, tx, new ManagedWord { Key = id + ":" + word.wordRank,
                        BookId = id, Word = word, Difficulty = 0.3, Interval = 3 });
                    tx.Commit();
                }
            }
            return id;
        }

        public byte[] ExportRecords(string from, string to, string book, string kind = null)
        {
            var range = DateRange(from, to);
            lock (Gate) using (var db = Open())
            {
                var rows = db.Query(@"SELECT e.AtUtc,e.Kind,e.Action,e.Correct,e.SessionId,w.HeadWord,w.Translation,b.Name
FROM StudyEvents e JOIN ManagedWords w ON w.Key=e.WordKey JOIN ManagedBooks b ON b.Id=w.BookId
WHERE e.AtUtc>=@from AND e.AtUtc<@to AND (@book='' OR w.BookId=@book) AND e.Kind<>'feedback' AND (" + EventKindCondition(kind) + @")
ORDER BY e.AtUtc LIMIT 100001", new { from = range.Item1, to = range.Item2, book = book ?? "" }).ToList();
                if (rows.Count > 100000) throw new ArgumentException("导出超过十万条，请缩小日期范围。");
                var workbook = new XSSFWorkbook();
                try
                {
                    var sheet = workbook.CreateSheet("学习记录");
                    string[] headers = { "时间", "单词", "翻译", "词库", "类型", "操作", "测验正确", "轮次" };
                    var header = sheet.CreateRow(0);
                    for (int i = 0; i < headers.Length; i++) { header.CreateCell(i).SetCellValue(headers[i]); sheet.SetColumnWidth(i, i == 2 ? 10000 : 6000); }
                    int index = 1;
                    foreach (var value in rows)
                    {
                        var row = sheet.CreateRow(index++);
                        string[] cells = { StudyJson.Local((string)value.AtUtc).ToString("yyyy-MM-dd HH:mm:ss"), (string)value.HeadWord,
                            (string)value.Translation, (string)value.Name, (string)value.Kind, (string)value.Action,
                            value.Correct == null ? "" : Convert.ToInt32(value.Correct) == 1 ? "是" : "否", (string)value.SessionId };
                        for (int i = 0; i < cells.Length; i++) row.CreateCell(i).SetCellValue(cells[i] ?? "");
                    }
                    using (var output = new MemoryStream()) { workbook.Write(output); return output.ToArray(); }
                }
                finally { workbook.Close(); }
            }
        }

        public void RestoreBackup(byte[] bytes, bool legacy = false)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 128 * 1024 * 1024) throw new ArgumentException("数据库文件应小于 128MB。");
            byte[] signature = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
            if (bytes.Length < signature.Length || !bytes.Take(signature.Length).SequenceEqual(signature))
                throw new ArgumentException("请选择有效的 SQLite 数据库文件。");
            lock (Gate)
            {
                if (Pending() != null) throw new InvalidOperationException("请先结束未完成的学习，再恢复数据。");
                string directory = Path.Combine(Root, "restore-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string uploaded = Path.Combine(directory, "uploaded.sqlite");
                try
                {
                    File.WriteAllBytes(uploaded, bytes);
                    using (var source = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = uploaded, ReadOnly = true,
                        FailIfMissing = true, Pooling = false }.ToString()))
                    {
                        source.Open();
                        if (source.ExecuteScalar<string>("PRAGMA integrity_check") != "ok") throw new ArgumentException("数据库文件损坏。");
                        if (source.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE type IN ('trigger','view') OR upper(sql) LIKE 'CREATE VIRTUAL TABLE%'") != 0)
                            throw new ArgumentException("不支持包含自定义触发器或视图的数据库。");
                        if (!legacy && source.ExecuteScalar<int>("SELECT Version FROM ManagementMeta") != 1)
                            throw new ArgumentException("备份版本不兼容。");
                        if (legacy && source.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name IN ('Global','Count','CET4_1')") != 3)
                            throw new ArgumentException("这不是兼容的旧版 ToastFish 数据库。");
                    }
                    string prepared = uploaded;
                    if (legacy)
                    {
                        using (var db = Open()) if (db.ExecuteScalar<int>("SELECT count(*) FROM StudyEvents") != 0)
                            throw new InvalidOperationException("已有新版本学习记录，不能用旧版数据库覆盖；请先备份并使用完整备份恢复。");
                        prepared = new StudyStore(Path.Combine(directory, "converted"), uploaded).DatabasePath;
                    }
                    using (var validate = OpenPath(prepared))
                    {
                        foreach (string table in new[] { "ManagedBooks", "ManagedWords", "StudySessions", "StudyEvents", "ManagementSettings" })
                            if (validate.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@table", new { table }) != 1)
                                throw new ArgumentException("备份缺少必要的数据表。");
                        if (validate.Query("PRAGMA foreign_key_check").Any()) throw new ArgumentException("备份数据关联损坏。");
                        string settingsJson = validate.ExecuteScalar<string>("SELECT Payload FROM ManagementSettings WHERE Id=1");
                        var settingsFields = StudyJson.Read<Dictionary<string, object>>(settingsJson ?? "{}");
                        if (settingsFields == null || new[] { "BookId", "Number", "EngType", "AutoPlay", "AutoLog" }.Any(k => !settingsFields.ContainsKey(k)))
                            throw new ArgumentException("备份缺少必要设置。");
                        var configuration = StudyJson.Read<ManagementSettings>(settingsJson);
                        if (configuration == null || configuration.Number < 1 || configuration.Number > 100 ||
                            (configuration.EngType != 1 && configuration.EngType != 2) ||
                            validate.ExecuteScalar<int>("SELECT count(*) FROM ManagedBooks WHERE Id=@id", new { id = configuration.BookId }) != 1)
                            throw new ArgumentException("备份中的设置不完整或无效。");
                        foreach (var row in validate.Query("SELECT Key,BookId,Payload FROM ManagedWords"))
                        {
                            var word = StudyJson.Read<ManagedWord>((string)row.Payload);
                            if (word == null || word.Key != (string)row.Key || word.BookId != (string)row.BookId ||
                                word.Word == null || string.IsNullOrWhiteSpace(word.Word.headWord) || word.Status < 0 || word.Status > 5 ||
                                double.IsNaN(word.Difficulty) || double.IsInfinity(word.Difficulty) || word.Difficulty < 0 || word.Difficulty > 1 ||
                                double.IsNaN(word.Interval) || double.IsInfinity(word.Interval) || word.Interval <= 0 || word.Interval > 36500)
                                throw new ArgumentException("备份中的单词状态无效。");
                            StudyJson.Local(word.LastReviewed); StudyJson.Local(word.LearningDue);
                        }
                        foreach (var payload in validate.Query<string>("SELECT Snapshot FROM StudySessions"))
                        {
                            var round = StudyJson.Read<StudyRound>(payload);
                            if (round == null || round.Cards == null || round.Cards.Count == 0 || round.Buttons == null ||
                                (!new[] { "running", "paused", "ended", "completed" }.Contains(round.State)) ||
                                (!new[] { "learn", "test", "feedback", "complete" }.Contains(round.Stage)) ||
                                ((round.State == "running" || round.State == "paused") && (round.CurrentKey == null || round.Text == null || round.Text.Length < 1 || round.Text.Length > 3)) ||
                                (round.CurrentKey != null && (!round.Cards.ContainsKey(round.CurrentKey) || round.Buttons.Count < 1 || round.Buttons.Count > 5)))
                                throw new ArgumentException("备份中的轮次快照不完整。");
                        }
                    }
                    BackupTo(Path.Combine(Root, "before-restore-" + Guid.NewGuid().ToString("N") + ".sqlite"));
                    using (var source = OpenPath(prepared)) using (var target = Open()) source.BackupDatabase(target, "main", "main", -1, null, 0);
                    using (var db = Open()) db.Execute("UPDATE StudySessions SET State='paused' WHERE State='running'");
                    ApplySettings(Settings());
                }
                finally
                {
                    if (Path.GetFullPath(directory).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(directory, true);
                }
            }
        }
    }
}
