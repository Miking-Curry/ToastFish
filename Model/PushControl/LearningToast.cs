using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ToastFish.Model.PushControl
{
    internal static class LearningToast
    {
        public static ToastContentBuilder Create(string[] paragraphs, LearningRequest request)
        {
            var builder = new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Reminder)
                .AddAudio(new ToastAudio { Silent = true })
                .AddArgument("learning", request.Id);
            foreach (string paragraph in paragraphs)
                if (!string.IsNullOrWhiteSpace(paragraph)) builder.AddText(paragraph);
            foreach (var action in request.Actions)
                builder.AddButton(new ToastButton()
                    .SetContent(action.Label)
                    .AddArgument("learning", request.Id)
                    .AddArgument("action", action.Value)
                    .SetBackgroundActivation());
            return builder;
        }
    }
}
