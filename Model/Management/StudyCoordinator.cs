using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using ToastFish.Model.Log;
using ToastFish.Model.PushControl;

namespace ToastFish.Model.Management
{
    internal sealed class StudyCoordinator
    {
        private static readonly Lazy<StudyCoordinator> defaultCoordinator = new Lazy<StudyCoordinator>(() => new StudyCoordinator(StudyStore.Default));
        public static StudyCoordinator Default => defaultCoordinator.Value;
        private readonly StudyStore store;
        private readonly StudyEngine engine;
        private bool managed;
        public bool Maintenance { get; private set; }
        public StudyCoordinator(StudyStore store) { this.store = store; engine = new StudyEngine(store); }

        public void Start(string book, int count, string mode = "normal", string[] keys = null)
        {
            if (Maintenance || LearningSession.IsActive) throw new InvalidOperationException("已有学习或数据维护操作，请稍后重试。");
            engine.Start(book, count, mode, keys);
            Run();
        }

        public void Resume()
        {
            if (Maintenance) throw new InvalidOperationException("正在维护数据，请稍后重试。");
            if (LearningSession.IsActive && !managed) throw new InvalidOperationException("请先完成托盘中的其他学习。");
            engine.Resume();
            if (LearningSession.IsActive) LearningSession.Resume(); else Run();
        }

        public void Pause()
        {
            if (Maintenance) throw new InvalidOperationException("正在维护数据，请稍后重试。");
            if (LearningSession.IsActive && !managed) throw new InvalidOperationException("当前模式尚不支持断点暂停。");
            engine.Pause();
            LearningSession.Pause();
        }

        public void End()
        {
            if (Maintenance) throw new InvalidOperationException("正在维护数据，请稍后重试。");
            if (LearningSession.IsActive && !managed) throw new InvalidOperationException("请先完成托盘中的其他学习。");
            engine.End();
            LearningSession.Stop();
        }

        public void RequireIdle()
        {
            if (Maintenance || LearningSession.IsActive || store.Pending() != null) throw new InvalidOperationException("请先结束学习并等待数据维护完成，再执行此操作。");
        }

        public void BeginMaintenance() { RequireIdle(); Maintenance = true; }
        public void EndMaintenance() { Maintenance = false; }

        private void Run()
        {
            managed = true;
            LearningSession.Start(() =>
            {
                try
                {
                    string lastSpokenOperation = null;
                    while (true)
                    {
                        StudyRound round;
                        lock (store.Gate) round = engine.Round;
                        if (round.State == "ended") return;
                        if (round.State == "completed")
                        {
                            if (store.Settings().AutoLog)
                            {
                                string directory = Path.Combine(store.Root, "Log");
                                Directory.CreateDirectory(directory);
                                new CreateLog().OutputExcel(Path.Combine(directory, round.Id + "_英语.xlsx"),
                                    round.Cards.Values.Select(c => c.Word).ToList(), "英语");
                            }
                            new PushWords().PushMessage("本轮学习已完成。" + (round.SkippedTests == 0 ? "" : "部分词条因缺少干扰选项未测验。"));
                            return;
                        }
                        string operation = round.OperationId;
                        LearningSession.ShowManaged(round.Text, round.Buttons.Select(b => new LearningAction(b.Label, b.Value)).ToArray(),
                            action => engine.Accept(operation, action));
                        if (round.Stage == "learn" && store.Settings().AutoPlay && lastSpokenOperation != operation)
                        {
                            lastSpokenOperation = operation;
                            Speak(round.Cards[round.CurrentKey].Word.headWord);
                        }
                        string value = LearningSession.WaitForAction().GetAwaiter().GetResult();
                        if (value == "voice") Speak(round.Cards[round.CurrentKey].Word.headWord);
                    }
                }
                finally
                {
                    lock (store.Gate)
                    {
                        if (engine.Round != null && engine.Round.State == "running") engine.Pause();
                        managed = false;
                    }
                }
            });
        }

        private void Speak(string word)
        {
            var settings = store.Settings();
            string accent = settings.EngType == 1 ? "us" : "uk";
            try
            {
                if (ToastFish.Model.Download.DownloadMp3.PlayMp3(new List<string> { word + "_" + accent,
                    Uri.EscapeDataString(word) + "&type=" + settings.EngType })) return;
                using (var speech = new SpeechSynthesizer()) speech.Speak(word);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("单词发音失败：" + ex.Message); }
        }
    }
}
