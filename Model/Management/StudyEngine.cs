using System;
using System.Collections.Generic;
using System.Linq;
using ToastFish.Model.SM2plus;

namespace ToastFish.Model.Management
{
    internal sealed class StudyEngine
    {
        private readonly StudyStore store;
        private readonly Random random = new Random();
        public StudyRound Round { get; private set; }

        public StudyEngine(StudyStore store) { this.store = store; }

        public void Start(string bookId, int number, string mode, string[] selected = null)
        {
            lock (store.Gate)
            {
                if (store.Pending() != null) throw new InvalidOperationException("请先继续或结束未完成的学习。");
                if (number < 1 || number > 100) throw new ArgumentException("每轮数量应为 1～100。");
                if (!new[] { "normal", "due", "selected", "wrong", "starred", "test" }.Contains(mode)) throw new ArgumentException("学习模式无效。");
                var words = store.GetWords(bookId);
                if (words.Count == 0) throw new ArgumentException("找不到英语词库。");
                var round = new StudyRound { BookId = bookId, Mode = mode };
                IEnumerable<ManagedWord> chosen;
                if (mode == "normal")
                {
                    var fresh = words.Where(w => w.Status != 5).OrderBy(w => random.Next()).Take(number).ToList();
                    var review = words.Where(w => w.Status == 5).OrderByDescending(w => w.ToCard().percentOverdue).Take(2 * number);
                    chosen = fresh.Concat(review);
                }
                else if (mode == "test") chosen = words.OrderBy(w => random.Next()).Take(number);
                else if (mode == "due")
                    chosen = words.Where(w => w.DueUtc != null && StudyJson.Local(w.DueUtc) <= DateTime.Now).OrderBy(w => w.DueUtc).Take(number);
                else
                {
                    var keys = mode == "selected" ? selected : store.SpecialKeys(bookId, mode);
                    if (keys == null || keys.Length == 0) throw new ArgumentException("请先选择单词。");
                    if (keys.Any(k => !words.Any(w => w.Key == k))) throw new ArgumentException("所选单词不属于当前词库。");
                    chosen = words.Where(w => keys.Contains(w.Key)).Take(number);
                }
                foreach (var word in chosen)
                {
                    word.Kind = !word.EverLearned ? "new" : word.Status == 5 ? "review" : "relearn";
                    word.Rated = false;
                    round.Cards.Add(word.Key, word);
                    if (word.Status == 5) round.Review.Add(word.Key); else round.New.Add(word.Key);
                }
                if (round.Cards.Count == 0) throw new InvalidOperationException("当前没有符合条件的单词。");
                if (mode == "test") { round.Stage = "test"; round.Tests = round.Cards.Keys.ToList(); round.New.Clear(); round.Review.Clear(); }
                Prepare(round);
                store.Save(round);
                Round = round;
            }
        }

        public void Resume()
        {
            lock (store.Gate)
            {
                var round = store.Pending() ?? throw new InvalidOperationException("没有未完成的学习。");
                round.State = "running";
                store.Save(round);
                Round = round;
            }
        }

        public void Pause()
        {
            lock (store.Gate)
            {
                var round = store.Pending();
                if (round == null) return;
                round.State = "paused";
                store.Save(round);
                Round = round;
            }
        }

        public void End()
        {
            lock (store.Gate)
            {
                var round = store.Pending();
                if (round == null) return;
                round.State = "ended";
                round.Completed = StudyJson.Time(DateTime.Now);
                store.Save(round);
                Round = round;
            }
        }

        public void Accept(string operationId, string action)
        {
            lock (store.Gate)
            {
                if (Round == null || Round.State != "running" || Round.OperationId != operationId)
                    throw new InvalidOperationException("这条通知已经失效，请恢复当前学习。");
                if (!Round.Buttons.Any(b => b.Value == action)) throw new ArgumentException("操作无效。");
                if (action == "voice") return;
                // 先计算副本，只有事务提交后才替换内存状态，写入失败可重试同一操作。
                var next = StudyJson.Read<StudyRound>(StudyJson.Write(Round));
                var word = next.Cards[next.CurrentKey];
                var entry = new StudyEvent { Id = operationId, SessionId = next.Id, WordKey = word.Key, Action = action };
                if (next.Stage == "learn")
                {
                    var scores = new Dictionary<string, double>
                    { { "again", Parameters.Again }, { "hard", Parameters.Hard }, { "good", Parameters.Good }, { "easy", Parameters.Easy } };
                    var card = word.ToCard();
                    card.updateCard(scores[action]);
                    word.Apply(card);
                    entry.Kind = word.Kind == "new" && word.Rated ? "learning" : word.Kind;
                    word.Rated = true;
                    next.New.Remove(word.Key);
                    next.Review.Remove(word.Key);
                    next.Learning.Remove(word.Key);
                    if (word.Status == 5) next.Finished.Add(word.Key); else next.Learning.Add(word.Key);
                    next.CurrentKey = null;
                }
                else if (next.Stage == "test")
                {
                    word.Rated = true;
                    entry.Kind = "test";
                    entry.Correct = action == next.CorrectIndex.ToString() ? 1 : 0;
                    if (entry.Correct == 1)
                    {
                        next.Tests.RemoveAt(0);
                        next.CurrentKey = null;
                    }
                    else
                    {
                        next.Stage = "feedback";
                        next.Text = new[] { "正确答案：" + next.Buttons[next.CorrectIndex].Label, next.Explanation };
                        next.Buttons = new List<StudyButton> { new StudyButton { Label = "继续", Value = "continue" } };
                        next.OperationId = Guid.NewGuid().ToString("N");
                    }
                }
                else if (next.Stage == "feedback")
                {
                    entry.Kind = "feedback";
                    next.Tests.RemoveAt(0);
                    next.Tests.Add(word.Key);
                    next.Stage = "test";
                    next.CurrentKey = null;
                }
                if (next.CurrentKey == null) Prepare(next);
                store.Save(next, entry, word);
                Round = next;
            }
        }

        private void Prepare(StudyRound round)
        {
            if (round.Stage == "learn")
            {
                round.Learning = round.Learning.OrderBy(k => StudyJson.Local(round.Cards[k].LearningDue)).ToList();
                string key = round.Learning.FirstOrDefault(k => StudyJson.Local(round.Cards[k].LearningDue) <= DateTime.Now);
                if (key == null) key = round.New.FirstOrDefault() ?? round.Review.FirstOrDefault() ?? round.Learning.FirstOrDefault();
                if (key != null)
                {
                    round.CurrentKey = key;
                    var word = round.Cards[key];
                    var settings = store.Settings();
                    string phone = settings.EngType == 1 ? word.Word.usPhone : word.Word.ukPhone;
                    string example = !string.IsNullOrWhiteSpace(word.Word.sentence) && word.Word.sentence.Length < 50 ?
                        word.Word.sentence + "\n" + word.Word.sentenceCN : word.Word.phrase + "\n" + word.Word.phraseCN;
                    round.Text = new[] { word.Word.headWord + "  (" + phone + ")\n" + word.Word.pos + ". " + word.Word.tranCN,
                        example, "新:" + round.New.Count + " 背:" + round.Learning.Count + " 复:" + round.Review.Count };
                    round.Buttons = new List<StudyButton>
                    {
                        new StudyButton { Label="没有印象",Value="again" }, new StudyButton { Label="记忆模糊",Value="hard" },
                        new StudyButton { Label="暂时记住",Value="good" }, new StudyButton { Label="已经牢记",Value="easy" },
                        new StudyButton { Label="发音",Value="voice" }
                    };
                    round.OperationId = Guid.NewGuid().ToString("N");
                    return;
                }
                round.Stage = "test";
                round.Tests = round.Finished.Where(k => round.Cards[k].LastScore != Parameters.Easy)
                    .OrderByDescending(k => round.Cards[k].ToCard().percentOverdue).ToList();
            }
            while (round.Tests.Count != 0)
            {
                string key = round.Tests[0];
                var word = round.Cards[key].Word;
                round.CurrentKey = key;
                int right;
                string[] labels;
                if (!string.IsNullOrWhiteSpace(word.question) && int.TryParse(word.rightIndex, out right) && right >= 1 && right <= 4 &&
                    new[] { word.choiceIndexOne, word.choiceIndexTwo, word.choiceIndexThree, word.choiceIndexFour }.All(x => !string.IsNullOrWhiteSpace(x)))
                {
                    round.Text = new[] { "选择题", word.question };
                    labels = new[] { word.choiceIndexOne, word.choiceIndexTwo, word.choiceIndexThree, word.choiceIndexFour };
                    round.CorrectIndex = right - 1;
                    round.Explanation = word.explain;
                }
                else
                {
                    var distractors = store.Distractors(round.BookId, key, word.headWord).ToList();
                    if (distractors.Count < 2) { round.Tests.RemoveAt(0); round.SkippedTests++; continue; }
                    round.CorrectIndex = random.Next(3);
                    distractors.Insert(round.CorrectIndex, word.headWord);
                    labels = distractors.ToArray();
                    round.Text = new[] { "翻译", word.tranCN };
                    round.Explanation = word.headWord + "：" + word.tranCN;
                }
                round.Buttons = labels.Select((label, index) => new StudyButton { Label = ((char)('A' + index)) + ". " + label, Value = index.ToString() }).ToList();
                round.OperationId = Guid.NewGuid().ToString("N");
                return;
            }
            round.Stage = "complete";
            round.State = "completed";
            round.Completed = StudyJson.Time(DateTime.Now);
            round.CurrentKey = null;
            round.Buttons.Clear();
        }
    }
}
