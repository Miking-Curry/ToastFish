using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Management
{
    internal sealed partial class StudyStore
    {
        public static readonly Dictionary<string, string> BuiltInBooks = new Dictionary<string, string>
        {
            {"CET4_1", "四级核心词汇"}, {"CET4_3", "四级完整词汇"}, {"CET6_1", "六级核心词汇"},
            {"CET6_3", "六级完整词汇"}, {"GMAT_3", "GMAT词汇"}, {"GRE_2", "GRE词汇"},
            {"IELTS_3", "IELTS词汇"}, {"TOEFL_2", "TOEFL词汇"}, {"SAT_2", "SAT词汇"},
            {"KaoYan_1", "考研必考词汇"}, {"KaoYan_2", "考研完整词汇"}, {"Level4_1", "专四真题高频词"},
            {"Level4luan_2", "专四核心词汇"}, {"Level8_1", "专八真题高频词"}, {"Level8luan_2", "专八核心词汇"}
        };
        private static readonly Lazy<StudyStore> defaultStore = new Lazy<StudyStore>(() => new StudyStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToastFish"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "inami.db")));
        public static StudyStore Default => defaultStore.Value;
        public readonly object Gate = new object();
        public string Root { get; }
        public string DatabasePath => Path.Combine(Root, "inami.db");

        public StudyStore(string root, string seed)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
            if (!File.Exists(DatabasePath))
            {
                if (!File.Exists(seed)) throw new FileNotFoundException("找不到初始词库。", seed);
                string temporary = DatabasePath + ".initializing";
                File.Copy(seed, temporary, true);
                using (var check = OpenPath(temporary))
                    if (check.ExecuteScalar<string>("PRAGMA integrity_check") != "ok")
                        throw new InvalidDataException("初始数据库损坏，原文件未修改。");
                File.Move(temporary, DatabasePath);
            }
            Initialize();
        }

        private static SQLiteConnection OpenPath(string path)
        {
            var db = new SQLiteConnection(new SQLiteConnectionStringBuilder
            { DataSource = path, Version = 3, Pooling = false, FailIfMissing = true, DefaultTimeout = 5 }.ToString());
            db.Open();
            db.Execute("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            return db;
        }
        public SQLiteConnection Open() => OpenPath(DatabasePath);

        private void Initialize()
        {
            lock (Gate)
            using (var db = Open())
            {
                bool existing = db.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='ManagementMeta'") > 0;
                if (existing)
                {
                    if (db.ExecuteScalar<int>("SELECT Version FROM ManagementMeta") != 1)
                        throw new InvalidDataException("用户数据库版本不兼容，请勿使用旧程序覆盖。");
                    db.Execute("UPDATE StudySessions SET State='paused' WHERE State='running'");
                    return;
                }
                BackupTo(Path.Combine(Root, "before-management-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".sqlite"));
                using (var tx = db.BeginTransaction())
                {
                    var globalFields = db.Query("PRAGMA table_info(Global)", transaction: tx).Select(r => (string)r.name).ToList();
                    if (!globalFields.Contains("EngType")) db.Execute("ALTER TABLE Global ADD COLUMN EngType INTEGER NOT NULL DEFAULT 2", transaction: tx);
                    if (!globalFields.Contains("autoLog")) db.Execute("ALTER TABLE Global ADD COLUMN autoLog INTEGER NOT NULL DEFAULT 1", transaction: tx);
                    db.Execute(@"CREATE TABLE ManagementMeta(Version INTEGER NOT NULL, HistorySince TEXT NOT NULL);
CREATE TABLE ManagedBooks(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Personal INTEGER NOT NULL DEFAULT 0);
CREATE TABLE ManagedWords(Key TEXT PRIMARY KEY, BookId TEXT NOT NULL REFERENCES ManagedBooks(Id), Rank INTEGER NOT NULL,
 HeadWord TEXT NOT NULL, Translation TEXT, Payload TEXT NOT NULL, Status INTEGER NOT NULL, DueUtc TEXT,
 EverLearned INTEGER NOT NULL DEFAULT 0, Starred INTEGER NOT NULL DEFAULT 0, WrongCount INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IX_ManagedWords_Book ON ManagedWords(BookId, Status);
CREATE INDEX IX_ManagedWords_Due ON ManagedWords(DueUtc);
CREATE TABLE StudySessions(Id TEXT PRIMARY KEY, BookId TEXT NOT NULL, Mode TEXT NOT NULL, State TEXT NOT NULL,
 Created TEXT NOT NULL, Completed TEXT, Snapshot TEXT NOT NULL);
CREATE UNIQUE INDEX IX_SingleSession ON StudySessions((1)) WHERE State IN ('running','paused');
CREATE TABLE StudyEvents(Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES StudySessions(Id),
 WordKey TEXT NOT NULL REFERENCES ManagedWords(Key), AtUtc TEXT NOT NULL, Kind TEXT NOT NULL, Action TEXT NOT NULL, Correct INTEGER);
CREATE INDEX IX_StudyEvents_Time ON StudyEvents(AtUtc, WordKey);
CREATE TABLE ManagementSettings(Id INTEGER PRIMARY KEY CHECK(Id=1), Payload TEXT NOT NULL);", transaction: tx);
                    db.Execute("INSERT INTO ManagementMeta VALUES(1,@time)", new { time = StudyJson.Time(DateTime.Now) }, tx);
                    foreach (var book in BuiltInBooks)
                    {
                        if (db.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name", new { name = book.Key }, tx) == 0) continue;
                        db.Execute("INSERT INTO ManagedBooks(Id,Name) VALUES(@Id,@Name)", new { Id = book.Key, Name = book.Value }, tx);
                        var fields = db.Query("PRAGMA table_info(" + book.Key + ")", transaction: tx).Select(r => (string)r.name).ToList();
                        foreach (var column in new[] { "difficulty REAL NOT NULL DEFAULT 0.3", "daysBetweenReviews REAL NOT NULL DEFAULT 3", "lastScore REAL NOT NULL DEFAULT 0", "dateLastReviewed TEXT" })
                            if (!fields.Contains(column.Split(' ')[0])) db.Execute("ALTER TABLE " + book.Key + " ADD COLUMN " + column, transaction: tx);
                        foreach (var word in db.Query<Word>("SELECT * FROM " + book.Key, transaction: tx).ToList())
                        {
                            DateTime last;
                            var state = new ManagedWord
                            {
                                Key = book.Key + ":" + word.wordRank, BookId = book.Key, Word = word,
                                Status = word.status, Difficulty = word.difficulty, Interval = word.daysBetweenReviews,
                                LastScore = word.lastScore, EverLearned = word.status != 0,
                                LastReviewed = DateTime.TryParse(word.dateLastReviewed, out last) ? StudyJson.Time(last) : null
                            };
                            InsertWord(db, tx, state);
                        }
                    }
                    var settings = new ManagementSettings();
                    var globals = db.Query<Global>("SELECT * FROM Global", transaction: tx).FirstOrDefault();
                    int number;
                    if (globals != null)
                    {
                        if (BuiltInBooks.ContainsKey(globals.currentBookName)) settings.BookId = globals.currentBookName;
                        if (int.TryParse(globals.currentWordNumber, out number) && number >= 1 && number <= 100) settings.Number = number;
                        settings.AutoPlay = globals.autoPlay != 0;
                        settings.EngType = globals.EngType == 1 ? 1 : 2;
                        settings.AutoLog = globals.autoLog != 0;
                    }
                    db.Execute("INSERT INTO ManagementSettings VALUES(1,@value)", new { value = StudyJson.Write(settings) }, tx);
                    tx.Commit();
                }
            }
        }

        private static void InsertWord(SQLiteConnection db, SQLiteTransaction tx, ManagedWord word)
        {
            db.Execute(@"INSERT INTO ManagedWords(Key,BookId,Rank,HeadWord,Translation,Payload,Status,DueUtc,EverLearned)
VALUES(@Key,@BookId,@Rank,@HeadWord,@Translation,@Payload,@Status,@DueUtc,@EverLearned)", new
            {
                word.Key, word.BookId, Rank = word.Word.wordRank, word.Word.headWord,
                Translation = word.Word.tranCN, Payload = StudyJson.Write(word), word.Status, word.DueUtc,
                EverLearned = word.EverLearned ? 1 : 0
            }, tx);
        }

        public List<ManagedWord> GetWords(string bookId)
        {
            lock (Gate) using (var db = Open())
                return db.Query<string>("SELECT Payload FROM ManagedWords WHERE BookId=@bookId ORDER BY Rank", new { bookId })
                    .Select(StudyJson.Read<ManagedWord>).ToList();
        }

        public string[] Distractors(string bookId, string key, string word)
        {
            lock (Gate) using (var db = Open()) return db.Query<string>(@"SELECT DISTINCT HeadWord FROM ManagedWords
WHERE BookId=@bookId AND Key<>@key AND HeadWord<>@word ORDER BY random() LIMIT 2", new { bookId, key, word }).ToArray();
        }

        public StudyRound Pending()
        {
            lock (Gate) using (var db = Open())
            {
                var row = db.Query("SELECT State,Snapshot FROM StudySessions WHERE State IN ('running','paused')").SingleOrDefault();
                if (row == null) return null;
                var round = StudyJson.Read<StudyRound>((string)row.Snapshot);
                round.State = (string)row.State;
                return round;
            }
        }

        public void Save(StudyRound round, StudyEvent entry = null, ManagedWord word = null)
        {
            lock (Gate) using (var db = Open()) using (var tx = db.BeginTransaction())
            {
                string command = db.ExecuteScalar<int>("SELECT count(*) FROM StudySessions WHERE Id=@id", new { id = round.Id }, tx) == 0 ?
                    @"INSERT INTO StudySessions(Id,BookId,Mode,State,Created,Completed,Snapshot) VALUES(@Id,@BookId,@Mode,@State,@Created,@Completed,@Snapshot)" :
                    @"UPDATE StudySessions SET State=@State,Completed=@Completed,Snapshot=@Snapshot WHERE Id=@Id";
                db.Execute(command, new
                { round.Id, round.BookId, round.Mode, round.State, round.Created, round.Completed, Snapshot = StudyJson.Write(round) }, tx);
                if (word != null)
                {
                    db.Execute(@"UPDATE ManagedWords SET Payload=@Payload,Status=@Status,DueUtc=@DueUtc,EverLearned=@EverLearned,
WrongCount=WrongCount+@Wrong WHERE Key=@Key", new { word.Key, Payload = StudyJson.Write(word), word.Status, word.DueUtc,
                        EverLearned = word.EverLearned ? 1 : 0, Wrong = entry?.Correct == 0 ? 1 : 0 }, tx);
                    if (BuiltInBooks.ContainsKey(word.BookId))
                    {
                        db.Execute("UPDATE " + word.BookId + @" SET status=@Status,difficulty=@Difficulty,daysBetweenReviews=@Interval,
lastScore=@LastScore,dateLastReviewed=@LastReviewed WHERE wordRank=@rank", new
                        { word.Status, word.Difficulty, word.Interval, word.LastScore, word.LastReviewed, rank = word.Word.wordRank }, tx);
                        db.Execute("UPDATE Count SET current=(SELECT count(*) FROM " + word.BookId + " WHERE status<>0) WHERE bookName=@id", new { id = word.BookId }, tx);
                    }
                }
                if (entry != null) db.Execute(@"INSERT INTO StudyEvents(Id,SessionId,WordKey,AtUtc,Kind,Action,Correct)
VALUES(@Id,@SessionId,@WordKey,@AtUtc,@Kind,@Action,@Correct)", entry, tx);
                tx.Commit();
            }
        }

        public ManagementSettings Settings()
        {
            lock (Gate) using (var db = Open())
                return StudyJson.Read<ManagementSettings>(db.ExecuteScalar<string>("SELECT Payload FROM ManagementSettings WHERE Id=1"));
        }

        public void BackupTo(string destination)
        {
            lock (Gate) using (var source = Open())
            using (var target = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = destination, Version = 3, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target, "main", "main", -1, null, 0);
            }
        }
    }
}
