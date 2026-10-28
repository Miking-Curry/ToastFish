using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace ToastFish.Model.PushControl
{
    internal static class LearningSession
    {
        private static readonly LearningInput input = new LearningInput();
        private static ToastContentBuilder currentToast;
        private static ToastNotification currentNotification;
        private static Task<string> pending;
        private static volatile bool shuttingDown;
        private static Action<string> beforeSubmit;
        private static bool paused;
        public static bool IsActive { get; private set; }
        public static bool HasCurrent => input.Current != null;

        static LearningSession()
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;
        }

        private static void OnActivated(ToastNotificationActivatedEventArgsCompat args)
        {
            if (shuttingDown) return;
            var arguments = ToastArguments.Parse(args.Argument);
            if (!arguments.Contains("learning")) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var request = input.Current;
                if (!shuttingDown && !paused && request != null && arguments["learning"] == request.Id)
                {
                    if (arguments.Contains("action")) Submit(request, arguments["action"]);
                    else Restore();
                }
            }));
        }

        // 会话由托盘的 UI 线程启动，工作线程只执行原有学习流程。
        public static void Start(Action work)
        {
            if (shuttingDown) return;
            if (IsActive) { Restore(); return; }
            paused = false;
            IsActive = true;
            new Thread(() =>
            {
                try { work(); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!shuttingDown)
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("学习已停止：" + ex.Message, "ToastFish"));
                }
                finally
                {
                    if (!shuttingDown)
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            input.End();
                            pending = null;
                            beforeSubmit = null;
                            ClearNotification();
                            IsActive = false;
                        });
                }
            }) { IsBackground = true }.Start();
        }

        public static void Show(string[] paragraphs, params LearningAction[] actions)
        {
            ShowManaged(paragraphs, actions, null);
        }

        public static void ShowManaged(string[] paragraphs, LearningAction[] actions, Action<string> commit)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (shuttingDown || !IsActive) throw new OperationCanceledException();
                var request = input.Begin(actions);
                pending = request.Completion.Task;
                beforeSubmit = commit;
                try
                {
                    currentToast = LearningToast.Create(paragraphs, request);
                    if (!paused) ShowNotification();
                }
                catch
                {
                    input.End();
                    throw;
                }
            });
        }

        public static Task<string> WaitForAction()
        {
            if (shuttingDown) throw new OperationCanceledException();
            return pending ?? throw new InvalidOperationException("没有正在显示的学习内容。");
        }

        private static void Submit(LearningRequest request, string action)
        {
            if (paused || request == null || !ReferenceEquals(input.Current, request) || request.Completion.Task.IsCompleted ||
                !System.Linq.Enumerable.Any(request.Actions, a => a.Value == action)) return;
            try { beforeSubmit?.Invoke(action); }
            catch (Exception ex)
            {
                MessageBox.Show("操作未保存，请重试。\n" + ex.Message, "ToastFish");
                Restore();
                return;
            }
            if (input.Submit(request, action)) ClearNotification();
        }

        private static void ShowNotification()
        {
            currentToast.Show(toast =>
            {
                toast.Tag = "learning";
                toast.Group = "ToastFish";
                currentNotification = toast;
            });
        }

        private static void ClearNotification()
        {
            var notification = currentNotification;
            currentNotification = null;
            currentToast = null;
            if (notification == null) return;
            try
            {
                ToastNotificationManagerCompat.CreateToastNotifier().Hide(notification);
                ToastNotificationManagerCompat.History.Remove("learning", "ToastFish");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("清理学习通知失败：" + ex.Message);
            }
        }

        public static void OnKey(string key)
        {
            var request = input.Current;
            if (request != null) Submit(request, request.ActionForKey(key));
        }

        public static void Feedback(params string[] paragraphs)
        {
            Show(paragraphs, new LearningAction("继续", "continue"));
            WaitForAction().GetAwaiter().GetResult();
        }

        public static void Restore()
        {
            if (!paused && IsActive && currentToast != null && input.Current != null &&
                !input.Current.Completion.Task.IsCompleted) ShowNotification();
        }

        public static void Pause()
        {
            paused = true;
            var saved = currentToast;
            ClearNotification();
            currentToast = saved;
        }

        public static void Resume()
        {
            paused = false;
            Restore();
        }

        public static void Stop()
        {
            input.End();
            ClearNotification();
        }

        public static void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            input.End();
            ClearNotification();
            ToastNotificationManagerCompat.OnActivated -= OnActivated;
            pending = null;
            IsActive = false;
        }
    }
}
