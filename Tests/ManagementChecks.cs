using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;
using Dapper;
using NPOI.XSSF.UserModel;
using ToastFish.Model.Management;

internal static class ManagementChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    private static void Reject(Action work, string name)
    {
        try { work(); } catch (Exception) { checks++; return; }
        throw new Exception("FAIL: " + name);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string seed = Path.Combine(root, "seed.db"); Seed(seed);
        try
        {
            var store = new StudyStore(Path.Combine(root, "user"), seed);
            RunChecks(store, seed);
            if (args.Contains("--real-seed"))
            {
                string repositorySeed = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Resources", "inami.db"));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var real = new StudyStore(Path.Combine(root, "real-seed"), repositorySeed);
                using (var db = real.Open())
                    Check(db.ExecuteScalar<int>("SELECT count(*) FROM ManagedWords") > 1000, "full repository dictionary migration");
                Console.WriteLine("Full dictionary migration: " + watch.ElapsedMilliseconds + " ms");
            }
            Console.WriteLine("PASS: " + checks + " data/engine checks");
            if (args.Contains("--serve"))
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var server = new ManagementServer(store, new StudyCoordinator(store), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web"));
                server.Start();
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser-context.json"), StudyJson.Write(new { token = server.TestToken, root }));
                Console.WriteLine("Fixture server ready at " + ManagementServer.Address);
                app.Run(); server.Dispose();
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Seed(string path)
    {
        using (var db = new SQLiteConnection("Data Source=" + path + ";Version=3;Pooling=False"))
        {
            db.Open();
            db.Execute(@"CREATE TABLE Global(currentBookName TEXT,currentWordNumber TEXT,autoPlay INTEGER,EngType INTEGER,autoLog INTEGER);
INSERT INTO Global VALUES('CET4_1','10',0,2,0);
CREATE TABLE Count(bookName TEXT,number INTEGER,current INTEGER); INSERT INTO Count VALUES('CET4_1',12,1);
CREATE TABLE CET4_1(wordRank INTEGER PRIMARY KEY,status INTEGER,headWord TEXT,tranCN TEXT,pos TEXT,ukPhone TEXT,usPhone TEXT,
sentence TEXT,sentenceCN TEXT,question TEXT,explain TEXT,rightIndex TEXT,choiceIndexOne TEXT,choiceIndexTwo TEXT,
choiceIndexThree TEXT,choiceIndexFour TEXT,dateLastReviewed TEXT);");
            for (int i = 1; i <= 12; i++) db.Execute(@"INSERT INTO CET4_1(wordRank,status,headWord,tranCN,pos,question,explain,
rightIndex,choiceIndexOne,choiceIndexTwo,choiceIndexThree,choiceIndexFour,dateLastReviewed)
VALUES(@i,@status,@word,@tran,'n.',@question,'这里是解析','1','A.正确选项','B.干扰选项','C.干扰选项','D.干扰选项',@reviewed)",
                new { i, status = i == 12 ? 5 : 0, word = new[] { "district", "journey", "memory", "garden", "river", "quiet", "window", "planet", "forest", "cloud", "future", "review" }[i - 1],
                    tran = "测试词义 " + i, question = i == 1 ? "Which option is correct?" : "", reviewed = DateTime.Now.AddDays(-10).ToString("O") });
        }
    }
    private static int Events(StudyStore store) { using (var db = store.Open()) return db.ExecuteScalar<int>("SELECT count(*) FROM StudyEvents"); }
    private static void RunChecks(StudyStore store, string seed)
    {
        byte[] original = File.ReadAllBytes(seed);
        Check(store.GetWords("CET4_1").Count == 12, "migration retains words");
        Check(store.GetWords("CET4_1").Last().EverLearned, "old progress baseline");
        Check(Directory.GetFiles(store.Root, "before-management-*.sqlite").Length == 1, "migration backup");
        var engine = new StudyEngine(store); engine.Start("CET4_1", 10, "normal");
        Check(engine.Round.Cards.Count == 11, "original new/review selection");
        string id = engine.Round.Id; var keys = engine.Round.Cards.Keys.ToArray();
        for (int i = 0; i < 3; i++) engine.Accept(engine.Round.OperationId, "easy");
        Check(Events(store) == 3 && engine.Round.Cards.Values.Count(w => w.Rated) == 3, "three answers immediately committed");
        string operation = engine.Round.OperationId, current = engine.Round.CurrentKey;
        var secondStore = new StudyStore(store.Root, seed);
        Check(secondStore.Pending().State == "paused", "restart pauses round");
        var restored = new StudyEngine(secondStore); restored.Resume();
        Check(restored.Round.Id == id && restored.Round.Cards.Keys.SequenceEqual(keys), "original selection restored");
        Check(restored.Round.OperationId == operation && restored.Round.CurrentKey == current, "exact pending prompt restored");
        Reject(() => restored.Start("CET4_1", 1, "normal"), "concurrent round rejected");
        restored.Pause(); Reject(() => restored.Accept(operation, "easy"), "paused input rejected"); restored.Resume();
        using (var db = store.Open()) db.Execute("CREATE TRIGGER RejectEvent BEFORE INSERT ON StudyEvents BEGIN SELECT RAISE(ABORT,'simulated write failure'); END");
        Reject(() => restored.Accept(operation, "easy"), "write failure reported");
        Check(restored.Round.OperationId == operation && store.Pending().OperationId == operation && Events(store) == 3, "transaction failure leaves memory and disk unchanged");
        using (var db = store.Open()) db.Execute("DROP TRIGGER RejectEvent");
        restored.Accept(operation, "easy"); Reject(() => restored.Accept(operation, "easy"), "stale action rejected");
        Check(Events(store) == 4, "no duplicate event"); restored.End(); Check(store.Pending() == null, "end preserves history");
        store.Reset("CET4_1");
        engine = new StudyEngine(store); engine.Start("CET4_1", 1, "selected", new[] { "CET4_1:1" });
        while (engine.Round.Stage == "learn") engine.Accept(engine.Round.OperationId, "good");
        Check(engine.Round.Stage == "test" && engine.Round.Buttons.Count == 4, "four-option quiz");
        string[] choices = engine.Round.Buttons.Select(b => b.Label).ToArray();
        restored = new StudyEngine(store); restored.Resume();
        Check(restored.Round.Buttons.Select(b => b.Label).SequenceEqual(choices), "quiz options survive restart");
        restored.Accept(restored.Round.OperationId, ((restored.Round.CorrectIndex + 1) % 4).ToString());
        Check(restored.Round.Stage == "feedback", "wrong answer feedback");
        string feedback = StudyJson.Write(restored.Round.Text);
        restored = new StudyEngine(new StudyStore(store.Root, seed)); restored.Resume();
        Check(restored.Round.Stage == "feedback" && StudyJson.Write(restored.Round.Text) == feedback, "feedback survives restart");
        restored.Accept(restored.Round.OperationId, "continue"); restored.Accept(restored.Round.OperationId, restored.Round.CorrectIndex.ToString());
        Check(store.Pending() == null && restored.Round.State == "completed", "manual continue and finish");
        store.Star("CET4_1:1", true); Check(store.SpecialKeys("CET4_1", "starred").Contains("CET4_1:1"), "starred list");
        Check(store.SpecialKeys("CET4_1", "wrong").Contains("CET4_1:1"), "wrong list");
        int history = Events(store); store.Reset("CET4_1");
        Check(Events(store) == history && store.GetWords("CET4_1").All(w => w.Status == 0), "reset preserves history");
        engine = new StudyEngine(store); engine.Start("CET4_1", 1, "selected", new[] { "CET4_1:1" });
        Check(engine.Round.Cards["CET4_1:1"].Kind == "relearn", "reset is not new learning"); engine.End();
        byte[] excel = EnglishExcel(); Check(store.PreviewEnglish(excel, "words.xlsx").Count == 2, "preview");
        Reject(() => store.PreviewEnglish(new byte[2], "bad.txt"), "invalid upload");
        string personal = store.ImportEnglish("我的英语", excel, "words.xlsx");
        Check(store.GetWords(personal).Count == 2 && store.GetWords("CET4_1").Count == 12, "independent personal book");
        engine = new StudyEngine(store); engine.Start(personal, 1, "normal");
        while (engine.Round.State == "running") engine.Accept(engine.Round.OperationId, "good");
        Check(engine.Round.SkippedTests == 1, "small book skips impossible quiz");
        string backup = Path.Combine(store.Root, "test-backup.sqlite"); store.BackupTo(backup); history = Events(store);
        store.RestoreBackup(File.ReadAllBytes(backup)); Check(Events(store) == history, "backup restore");
        Reject(() => store.RestoreBackup(new byte[] { 1, 2, 3 }), "corrupt restore"); Check(Events(store) == history, "bad restore retains data");
        Reject(() => store.RestoreBackup(File.ReadAllBytes(seed), true), "legacy cannot erase new history");
        var range = StudyStore.DateRange("2026-10-19", "2026-10-19"); Check(StudyJson.Local(range.Item2).Date == new DateTime(2026, 10, 20), "local date boundary");
        Check(StudyJson.Local(StudyStore.DateRange("2026-10-18", "2026-10-18").Item2).DayOfWeek == DayOfWeek.Monday, "week boundary");
        Reject(() => StudyStore.DateRange("2026-10-20", "2026-10-19"), "bad date range");
        Check(store.ExportRecords(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), DateTime.Today.ToString("yyyy-MM-dd"), "").Length > 0, "record export");
        Check(File.ReadAllBytes(seed).SequenceEqual(original), "seed unchanged");
        Check(ManagementServer.FixedEquals("abc", "abc") && !ManagementServer.FixedEquals("abc", "abd") && !ManagementServer.FixedEquals(null, "abc"), "token comparison");
        StudyJson.Write(store.Dashboard()); StudyJson.Write(store.Words("", "", "", 1)); StudyJson.Write(store.WordDetail("CET4_1:1"));
        var preferences = store.Settings(); preferences.Number = 7; store.SaveSettings(preferences);
        using (var db = store.Open()) Check(db.ExecuteScalar<int>("SELECT currentWordNumber FROM Global") == 7, "management settings update legacy configuration");
        engine = new StudyEngine(store); engine.Start("CET4_1", 1, "test");
        Check(engine.Round.Stage == "test", "random test uses persistent engine"); engine.End();
        using (var source = new SQLiteConnection("Data Source=" + backup + ";Version=3;Pooling=False"))
        {
            source.Open(); source.Execute("UPDATE ManagementSettings SET Payload='{}'");
        }
        Reject(() => store.RestoreBackup(File.ReadAllBytes(backup)), "invalid settings rejected before restore");
        Check(store.Settings().Number == 7, "failed restore preserves current settings");
        var empty = new StudyStore(Path.Combine(store.Root, "legacy-check"), seed);
        empty.RestoreBackup(File.ReadAllBytes(seed), true);
        Check(empty.GetWords("CET4_1").Count == 12, "legacy database migration");
    }
    private static byte[] EnglishExcel()
    {
        var workbook = new XSSFWorkbook(); var sheet = workbook.CreateSheet("Word"); var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("英语"); header.CreateCell(1).SetCellValue("单词"); header.CreateCell(2).SetCellValue("翻译");
        for (int i = 1; i <= 2; i++) { var row = sheet.CreateRow(i); row.CreateCell(1).SetCellValue("personal" + i); row.CreateCell(2).SetCellValue("个人单词" + i); }
        using (var stream = new MemoryStream()) { workbook.Write(stream); workbook.Close(); return stream.ToArray(); }
    }
}
