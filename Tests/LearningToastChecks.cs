using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Toolkit.Uwp.Notifications;
using ToastFish.Model.PushControl;

internal static class LearningToastChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks++;
    }

    private static int Main()
    {
        try
        {
            var input = new LearningInput();
            var request = input.Begin(new LearningAction("没有印象", "again"),
                new LearningAction("记忆模糊", "hard"), new LearningAction("暂时记住", "good"),
                new LearningAction("已经牢记", "easy"), new LearningAction("发音", "voice"));
            var xml = XElement.Parse(LearningToast.Create(new[] { "district", "n. 区", "新:10 背:0 复:0" }, request)
                .GetToastContent().GetContent());
            Check((string)xml.Attribute("scenario") == "reminder", "native persistent reminder");
            Check((string)xml.Element("audio").Attribute("silent") == "true", "no repeated reminder sound");
            Check(xml.Descendants("text").Select(e => e.Value).SequenceEqual(new[] { "district", "n. 区", "新:10 背:0 复:0" }), "content retained");
            var buttons = xml.Descendants("action").ToArray();
            Check(buttons.Length == 5, "all scoring and voice buttons present");
            for (int i = 0; i < buttons.Length; i++)
            {
                var args = ToastArguments.Parse((string)buttons[i].Attribute("arguments"));
                Check(args["learning"] == request.Id && args["action"] == request.Actions[i].Value,
                    "button belongs to current question");
            }
            Check(request.ActionForKey("S") == "voice", "voice shortcut");
            Check(input.Submit(request, "good") && !input.Submit(request, "easy"), "duplicate submission ignored");
            var feedback = input.Begin(new LearningAction("继续", "continue"));
            Check(!input.Submit(request, "again") && !feedback.Completion.Task.IsCompleted, "stale question ignored");
            Check(feedback.ActionForKey("2") == null && feedback.ActionForKey("S") == null, "invalid shortcuts ignored");
            Check(!input.Submit(feedback, feedback.ActionForKey("2")), "invalid key does not answer");
            var feedbackXml = XElement.Parse(LearningToast.Create(new[] { "正确答案", "解析" }, feedback).GetToastContent().GetContent());
            Check((string)feedbackXml.Attribute("scenario") == "reminder" &&
                feedbackXml.Descendants("action").Count() == 1, "persistent feedback with manual continue");
            Check(input.Submit(feedback, feedback.ActionForKey("1")), "manual continue accepted");
            var pending = input.Begin(new LearningAction("A", "0"), new LearningAction("B", "1"), new LearningAction("C", "2"));
            Check(pending.ActionForKey("4") == null, "three-option question ignores fourth key");
            input.End();
            Check(pending.Completion.Task.IsCanceled && input.Current == null, "shutdown releases pending request");
            Console.WriteLine("PASS: " + checks + " native toast/input checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
