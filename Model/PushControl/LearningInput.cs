using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ToastFish.Model.PushControl
{
    internal sealed class LearningAction
    {
        public string Label { get; }
        public string Value { get; }

        public LearningAction(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    internal sealed class LearningRequest
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IReadOnlyList<LearningAction> Actions { get; }
        public TaskCompletionSource<string> Completion { get; } =
            new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        public LearningRequest(LearningAction[] actions)
        {
            if (actions == null || actions.Length == 0 ||
                actions.Any(a => a == null || string.IsNullOrEmpty(a.Value)))
                throw new ArgumentException("学习内容必须包含有效操作。", nameof(actions));
            Actions = Array.AsReadOnly((LearningAction[])actions.Clone());
        }

        public string ActionForKey(string key)
        {
            if (key == "S")
                return Actions.FirstOrDefault(a => a.Value == "voice")?.Value;
            int index;
            if (int.TryParse(key, out index) && index >= 1 && index <= 4 && index <= Actions.Count)
                return Actions[index - 1].Value;
            return null;
        }
    }

    // 所有输入在 UI 线程串行处理，旧按钮持有的请求不能提交新题。
    internal sealed class LearningInput
    {
        public LearningRequest Current { get; private set; }

        public LearningRequest Begin(params LearningAction[] actions)
        {
            End();
            Current = new LearningRequest(actions);
            return Current;
        }

        public bool Submit(LearningRequest request, string action)
        {
            if (request == null || !ReferenceEquals(request, Current) ||
                !request.Actions.Any(a => a.Value == action))
                return false;
            return request.Completion.TrySetResult(action);
        }

        public void End()
        {
            Current?.Completion.TrySetCanceled();
            Current = null;
        }
    }
}
