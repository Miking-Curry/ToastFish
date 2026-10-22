using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dapper;
using ToastFish.Model.SM2plus;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Management
{
    internal sealed partial class StudyStore
    {
        private static Dictionary<string, object> Row(object value) =>
            new Dictionary<string, object>((IDictionary<string, object>)value);

        public object Books()
        {
            lock (Gate) using (var db = Open()) return db.Query(@"SELECT b.Id id,b.Name name,b.Personal personal,+count(w.Key) total,coalesce(sum(w.Status<>0),0) learned,coalesce(sum(w.Status=5),0) reviewed
FROM ManagedBooks b LEFT JOIN ManagedWords w ON w.BookId=b.Id GROUP BY b.Id ORDER BY b.Personal,b.rowid")
                .Select(Row).ToArray();
        }

        public string[] SpecialKeys(string bookId, string mode)
        {
            lock (Gate) using (var db = Open()) return db.Query<string>("SELECT Key FROM ManagedWords WHERE BookId=@bookId AND " +
                (mode == "starred" ? "Starred=1" : "WrongCount>0"), new { bookId }).ToArray();
        }

        private static string ReviewWindowCondition(string window)
        {
            switch (window ?? "")
            {
                case "": return "1=1";
                case "overdue": return "w.Status=5 AND w.DueUtc<=@now";
                case "today": return "w.Status=5 AND w.DueUtc>@now AND w.DueUtc<@day1";
                case "tomorrow": return "w.Status=5 AND w.DueUtc>=@day1 AND w.DueUtc<@day2";
                case "week": return "w.Status=5 AND w.DueUtc>=@day2 AND w.DueUtc<@day7";
                case "fortnight": return "w.Status=5 AND w.DueUtc>=@day7 AND w.DueUtc<@day14";
                case "month": return "w.Status=5 AND w.DueUtc>=@day14 AND w.DueUtc<@day31";
                case "later": return "w.Status=5 AND w.DueUtc>=@day31";
                case "unscheduled": return "w.Status=5 AND w.DueUtc IS NULL";
                default: throw new ArgumentException("复习时间筛选条件无效。");
            }
        }

        public object ReviewOverview(string book)
        {
            string[] ids = { "overdue", "today", "tomorrow", "week", "fortnight", "month", "later", "unscheduled" };
            string[] labels = { "已到期", "今天", "明天", "2～6天", "7～13天", "14～30天", "31天+", "未排期" };
            var now = DateTime.Now;
            var today = now.Date;
            var args = new { book = book ?? "", now = StudyJson.Time(now), day1 = StudyJson.Time(today.AddDays(1)),
                day2 = StudyJson.Time(today.AddDays(2)), day7 = StudyJson.Time(today.AddDays(7)),
                day14 = StudyJson.Time(today.AddDays(14)), day31 = StudyJson.Time(today.AddDays(31)) };
            string cases = string.Join(" ", ids.Select(id => "WHEN " + ReviewWindowCondition(id) + " THEN '" + id + "'"));
            lock (Gate) using (var db = Open())
            {
                var rows = db.Query("SELECT CASE " + cases + " END bucket,count(*) count FROM ManagedWords w " +
                    "WHERE w.Status=5 AND (@book='' OR w.BookId=@book) GROUP BY bucket", args)
                    .ToDictionary(r => (string)r.bucket, r => Convert.ToInt32(r.count));
                var buckets = ids.Select((id, i) => new { id, label = labels[i], count = rows.ContainsKey(id) ? rows[id] : 0 }).ToArray();
                return new { algorithm = "SM2plus", generatedAt = StudyJson.Time(now), total = buckets.Sum(b => b.count), buckets };
            }
        }

        public object Words(string book, string q, string filter, int page, string window = null)
        {
            q = (q ?? "").Trim();
            if (q.Length > 200) throw new ArgumentException("搜索内容过长。");
            string condition;
            switch (filter ?? "")
            {
                case "new": condition = "w.Status=0"; break;
                case "learning": condition = "w.Status BETWEEN 1 AND 4"; break;
                case "review": condition = "w.Status=5"; break;
                case "starred": condition = "w.Starred=1"; break;
                case "wrong": condition = "w.WrongCount>0"; break;
                case "forgotten": condition = "EXISTS(SELECT 1 FROM StudyEvents e WHERE e.WordKey=w.Key AND e.Action='again')"; break;
                case "due": condition = "w.DueUtc IS NOT NULL AND w.DueUtc<=@now"; break;
                case "": condition = "1=1"; break;
                default: throw new ArgumentException("单词筛选条件无效。");
            }
            if (page < 1 || page > 100000) throw new ArgumentException("页码无效。");
            var now = DateTime.Now;
            var today = now.Date;
            var args = new { book = book ?? "", q = "%" + q + "%", now = StudyJson.Time(now), offset = (page - 1) * 50,
                day1 = StudyJson.Time(today.AddDays(1)), day2 = StudyJson.Time(today.AddDays(2)), day7 = StudyJson.Time(today.AddDays(7)),
                day14 = StudyJson.Time(today.AddDays(14)), day31 = StudyJson.Time(today.AddDays(31)) };
            string where = " WHERE (@book='' OR w.BookId=@book) AND (w.HeadWord LIKE @q OR w.Translation LIKE @q) AND (" + condition +
                ") AND (" + ReviewWindowCondition(window) + ")";
            lock (Gate) using (var db = Open()) return new
            {
                items = db.Query(@"SELECT w.Key key,w.BookId bookId,b.Name bookName,w.HeadWord headWord,w.Translation translation,
w.Status status,w.DueUtc dueUtc,w.Starred starred,w.WrongCount wrongCount FROM ManagedWords w JOIN ManagedBooks b ON b.Id=w.BookId" +
                    where + (string.IsNullOrEmpty(window) ? " ORDER BY w.BookId,w.Rank" : " ORDER BY w.DueUtc,w.Key") +
                    " LIMIT 50 OFFSET @offset", args).Select(Row).ToArray(),
                count = db.ExecuteScalar<int>("SELECT count(*) FROM ManagedWords w" + where, args), page, pageSize = 50
            };
        }

        public object WordDetail(string key)
        {
            lock (Gate) using (var db = Open())
            {
                var word = db.Query("SELECT Payload,Starred,WrongCount FROM ManagedWords WHERE Key=@key", new { key }).SingleOrDefault();
                if (word == null) throw new ArgumentException("找不到单词。");
                return new { word = StudyJson.Read<ManagedWord>((string)word.Payload), starred = (long)word.Starred != 0,
                    wrongCount = (long)word.WrongCount, events = db.Query(@"SELECT Id id,AtUtc atUtc,Kind kind,Action action,Correct correct,
SessionId sessionId FROM StudyEvents WHERE WordKey=@key ORDER BY AtUtc DESC LIMIT 100", new { key }).Select(Row).ToArray() };
            }
        }

        public void Star(string key, bool starred)
        {
            lock (Gate) using (var db = Open())
                if (db.Execute("UPDATE ManagedWords SET Starred=@value WHERE Key=@key", new { key, value = starred ? 1 : 0 }) == 0)
                    throw new ArgumentException("找不到单词。");
        }

        public static Tuple<string, string> DateRange(string from, string to)
        {
            DateTime start, end;
            if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) ||
                !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end) || end < start || (end - start).TotalDays > 366)
                throw new ArgumentException("请选择一年以内的有效日期范围。");
            return Tuple.Create(StudyJson.Time(start), StudyJson.Time(end.AddDays(1)));
        }

        private static string EventKindCondition(string kind)
        {
            switch (kind ?? "")
            {
                case "": return "1=1";
                case "ratings": return "e.Kind IN ('new','learning','review','relearn')";
                case "test": return "e.Kind='test'";
                default: throw new ArgumentException("记录类型筛选条件无效。");
            }
        }

        public object Records(string from, string to, string book, int page, string kind = null)
        {
            var range = DateRange(from, to);
            if (page < 1 || page > 100000) throw new ArgumentException("页码无效。");
            var args = new { from = range.Item1, to = range.Item2, book = book ?? "", offset = (page - 1) * 50 };
            string where = " WHERE e.AtUtc>=@from AND e.AtUtc<@to AND (@book='' OR w.BookId=@book) AND e.Kind<>'feedback' AND (" + EventKindCondition(kind) + ")";
            lock (Gate) using (var db = Open()) return new
            {
                items = db.Query(@"SELECT e.Id id,e.AtUtc atUtc,e.Kind kind,e.Action action,e.Correct correct,e.SessionId sessionId,
w.Key key,w.HeadWord headWord,w.Translation translation,b.Name bookName FROM StudyEvents e
JOIN ManagedWords w ON w.Key=e.WordKey JOIN ManagedBooks b ON b.Id=w.BookId" + where + " ORDER BY e.AtUtc DESC LIMIT 50 OFFSET @offset", args).Select(Row).ToArray(),
                count = db.ExecuteScalar<int>("SELECT count(*) FROM StudyEvents e JOIN ManagedWords w ON w.Key=e.WordKey" + where, args),
                sessions = db.Query(@"SELECT s.Id id,s.BookId bookId,b.Name bookName,s.State state,s.Mode mode,s.Created created,s.Completed completed
FROM StudySessions s JOIN ManagedBooks b ON b.Id=s.BookId WHERE s.Created<@to AND coalesce(s.Completed,@to)>=@from
AND (@book='' OR s.BookId=@book) ORDER BY s.Created DESC LIMIT 100", args).Select(Row).ToArray(), page, pageSize = 50
            };
        }

        public object RoundDetail(string id)
        {
            lock (Gate) using (var db = Open())
            {
                var row = db.Query("SELECT Snapshot,State FROM StudySessions WHERE Id=@id", new { id }).SingleOrDefault();
                if (row == null) throw new ArgumentException("找不到学习轮次。");
                var round = StudyJson.Read<StudyRound>((string)row.Snapshot);
                round.State = (string)row.State;
                return new { round, events = db.Query<StudyEvent>("SELECT * FROM StudyEvents WHERE SessionId=@id ORDER BY AtUtc", new { id }).ToArray() };
            }
        }

        public object Dashboard()
        {
            DateTime today = DateTime.Today;
            DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            lock (Gate) using (var db = Open())
            {
                var events = db.Query<StudyEvent>("SELECT * FROM StudyEvents WHERE AtUtc>=@start AND AtUtc<@end",
                    new { start = StudyJson.Time(monday), end = StudyJson.Time(monday.AddDays(7)) }).ToList();
                Func<IEnumerable<StudyEvent>, object> counts = list => new
                {
                    newCount = list.Where(e => e.Kind == "new").Select(e => e.WordKey).Distinct().Count(),
                    reviewCount = list.Where(e => e.Kind == "review" || e.Kind == "relearn").Select(e => e.WordKey).Distinct().Count(),
                    actions = list.Count(e => e.Kind != "feedback")
                };
                return new
                {
                    today = counts(events.Where(e => StudyJson.Local(e.AtUtc).Date == today)), week = counts(events),
                    days = Enumerable.Range(0, 7).Select(i => new { date = monday.AddDays(i).ToString("yyyy-MM-dd"),
                        counts = counts(events.Where(e => StudyJson.Local(e.AtUtc).Date == monday.AddDays(i))) }).ToArray(),
                    due = db.ExecuteScalar<int>("SELECT count(*) FROM ManagedWords WHERE DueUtc<=@now", new { now = StudyJson.Time(DateTime.Now) }),
                    historySince = db.ExecuteScalar<string>("SELECT HistorySince FROM ManagementMeta"),
                    dataPath = Root, session = SessionSummary(), settings = Settings(), books = Books()
                };
            }
        }

        public object SessionSummary()
        {
            var round = Pending();
            if (round == null) return null;
            return new { id = round.Id, bookId = round.BookId, state = round.State, stage = round.Stage, total = round.Cards.Count,
                rated = round.Cards.Values.Count(c => c.Rated), finished = round.Finished.Count, current = round.CurrentKey == null ? null : round.Cards[round.CurrentKey].Word.headWord,
                remainingTests = round.Tests.Count, skippedTests = round.SkippedTests };
        }

        public void SaveSettings(ManagementSettings value, bool updateLegacy = true)
        {
            if (value.Number < 1 || value.Number > 100 || (value.EngType != 1 && value.EngType != 2)) throw new ArgumentException("设置值无效。");
            lock (Gate) using (var db = Open())
            {
                if (db.ExecuteScalar<int>("SELECT count(*) FROM ManagedBooks WHERE Id=@id", new { id = value.BookId }) == 0)
                    throw new ArgumentException("词库不存在。");
                using (var tx = db.BeginTransaction())
                {
                    db.Execute("UPDATE ManagementSettings SET Payload=@value WHERE Id=1", new { value = StudyJson.Write(value) }, tx);
                    if (updateLegacy) db.Execute(@"UPDATE Global SET currentWordNumber=@Number,EngType=@EngType,autoPlay=@play,autoLog=@log,
currentBookName=@book", new { value.Number, value.EngType, play = value.AutoPlay ? 1 : 0, log = value.AutoLog ? 1 : 0,
                        book = BuiltInBooks.ContainsKey(value.BookId) ? value.BookId : "CET4_1" }, tx);
                    tx.Commit();
                }
            }
            if (updateLegacy) ApplySettings(value);
        }

        public void SyncLegacySettings()
        {
            var value = Settings();
            value.Number = Select.WORD_NUMBER;
            value.EngType = Select.ENG_TYPE;
            value.AutoPlay = Select.AUTO_PLAY != 0;
            value.AutoLog = Select.AUTO_LOG != 0;
            SaveSettings(value, false);
        }

        public static void ApplySettings(ManagementSettings value)
        {
            Select.WORD_NUMBER = value.Number;
            Select.ENG_TYPE = value.EngType;
            Select.AUTO_PLAY = value.AutoPlay ? 1 : 0;
            Select.AUTO_LOG = value.AutoLog ? 1 : 0;
            if (BuiltInBooks.ContainsKey(value.BookId)) Select.TABLE_NAME = value.BookId;
        }

        public void Reset(string bookId)
        {
            lock (Gate)
            {
                if (Pending() != null) throw new InvalidOperationException("请先结束未完成的学习。");
                var words = GetWords(bookId);
                if (words.Count == 0) throw new ArgumentException("词库不存在。");
                BackupTo(Path.Combine(Root, "before-reset-" + Guid.NewGuid().ToString("N") + ".sqlite"));
                using (var db = Open()) using (var tx = db.BeginTransaction())
                {
                    foreach (var word in words)
                    {
                        word.Status = 0; word.Difficulty = Parameters.diffcultyDefaultValue;
                        word.Interval = Parameters.daysBetweenReviewsDefaultValue; word.LastScore = 0;
                        word.LastReviewed = null; word.LearningDue = null; word.Rated = false;
                        db.Execute("UPDATE ManagedWords SET Status=0,DueUtc=NULL,Payload=@payload WHERE Key=@key", new { payload = StudyJson.Write(word), key = word.Key }, tx);
                    }
                    if (BuiltInBooks.ContainsKey(bookId))
                    {
                        db.Execute("UPDATE " + bookId + " SET status=0,difficulty=0.3,daysBetweenReviews=3,lastScore=0,dateLastReviewed=NULL", transaction: tx);
                        db.Execute("UPDATE Count SET current=0 WHERE bookName=@bookId", new { bookId }, tx);
                    }
                    tx.Commit();
                }
            }
        }
    }
}
