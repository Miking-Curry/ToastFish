using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using ToastFish.Model.SM2plus;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Management
{
    internal static class StudyJson
    {
        public static string Write(object value) => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(value);
        public static T Read<T>(string value) => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Deserialize<T>(value);
        public static string Time(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        public static DateTime Local(string value) => string.IsNullOrEmpty(value) ? DateTime.MinValue :
            DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
    }

    internal sealed class ManagedWord
    {
        public string Key { get; set; }
        public string BookId { get; set; }
        public Word Word { get; set; }
        public int Status { get; set; }
        public double Difficulty { get; set; }
        public double Interval { get; set; }
        public double LastScore { get; set; }
        public string LastReviewed { get; set; }
        public string LearningDue { get; set; }
        public bool EverLearned { get; set; }
        public string Kind { get; set; }
        public bool Rated { get; set; }

        public Card ToCard() => new Card
        {
            word = Word, status = (Cardstatus)Status, difficulty = Difficulty,
            daysBetweenReviews = Interval, lastScore = LastScore,
            dateLastReviewed = StudyJson.Local(LastReviewed), dateLearingDue = StudyJson.Local(LearningDue)
        };

        public void Apply(Card card)
        {
            Status = (int)card.status;
            Difficulty = card.difficulty;
            Interval = card.daysBetweenReviews;
            LastScore = card.lastScore;
            LastReviewed = card.dateLastReviewed == DateTime.MinValue ? null : StudyJson.Time(card.dateLastReviewed);
            LearningDue = card.dateLearingDue == DateTime.MinValue ? null : StudyJson.Time(card.dateLearingDue);
            EverLearned = true;
        }

        public string DueUtc => Status == 5 && LastReviewed != null ?
            StudyJson.Time(StudyJson.Local(LastReviewed).AddDays(Math.Min(Interval, 36500))) : null;
    }

    internal sealed class StudyRound
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string BookId { get; set; }
        public string Mode { get; set; } = "normal";
        public string State { get; set; } = "running";
        public string Created { get; set; } = StudyJson.Time(DateTime.Now);
        public string Completed { get; set; }
        public string Stage { get; set; } = "learn";
        public string OperationId { get; set; }
        public string CurrentKey { get; set; }
        public Dictionary<string, ManagedWord> Cards { get; set; } = new Dictionary<string, ManagedWord>();
        public List<string> New { get; set; } = new List<string>();
        public List<string> Review { get; set; } = new List<string>();
        public List<string> Learning { get; set; } = new List<string>();
        public List<string> Finished { get; set; } = new List<string>();
        public List<string> Tests { get; set; } = new List<string>();
        public string[] Text { get; set; }
        public List<StudyButton> Buttons { get; set; } = new List<StudyButton>();
        public int CorrectIndex { get; set; }
        public string Explanation { get; set; }
        public int SkippedTests { get; set; }
    }

    internal sealed class StudyButton
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    internal sealed class StudyEvent
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string WordKey { get; set; }
        public string AtUtc { get; set; } = StudyJson.Time(DateTime.Now);
        public string Kind { get; set; }
        public string Action { get; set; }
        public int? Correct { get; set; }
    }

    internal sealed class ManagementSettings
    {
        public string BookId { get; set; } = "CET4_1";
        public int Number { get; set; } = 10;
        public int EngType { get; set; } = 2;
        public bool AutoPlay { get; set; } = true;
        public bool AutoLog { get; set; } = true;
    }
}
