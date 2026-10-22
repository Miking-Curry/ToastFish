using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ToastFish.Model.Management
{
    internal sealed class ManagementServer : IDisposable
    {
        public const string Address = "http://127.0.0.1:17653/";
        private readonly StudyStore store;
        private readonly StudyCoordinator coordinator;
        private readonly string webRoot;
        private readonly HttpListener listener = new HttpListener();
        private readonly SemaphoreSlim requests = new SemaphoreSlim(8);
        private readonly string token;
        internal string TestToken => token;
        private bool disposed;

        public ManagementServer(StudyStore store, StudyCoordinator coordinator, string webRoot)
        {
            this.store = store; this.coordinator = coordinator; this.webRoot = Path.GetFullPath(webRoot);
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            token = Convert.ToBase64String(bytes);
            listener.Prefixes.Add(Address);
        }

        public void OpenBrowser()
        {
            if (!File.Exists(Path.Combine(webRoot, "index.html"))) throw new InvalidOperationException("管理页尚未构建，请先在 Web 目录执行 npm ci 和 npm run build。");
            if (!listener.IsListening)
            {
                try { Start(); }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5)
                {
                    if (MessageBox.Show("首次启用本地管理页，需要为当前用户注册本机地址访问权限。仅本次授权需要管理员权限。是否继续？",
                        "ToastFish", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    using (var helper = Process.Start(new ProcessStartInfo
                    {
                        FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                        Arguments = "--authorize-management " + WindowsIdentity.GetCurrent().User.Value,
                        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                    }))
                    {
                        if (!helper.WaitForExit(30000) || helper.ExitCode != 0) throw new InvalidOperationException("本地地址授权未完成，请重试。");
                    }
                    Start();
                }
            }
            Process.Start(new ProcessStartInfo(Address + "#token=" + Uri.EscapeDataString(token)) { UseShellExecute = true });
        }

        public static int Authorize(string sid)
        {
            if (string.IsNullOrEmpty(sid) || !System.Text.RegularExpressions.Regex.IsMatch(sid, @"^S-1-5-21-\d+-\d+-\d+-\d+$")) return 2;
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = "http add urlacl url=" + Address + " sddl=D:(A;;GX;;;" + sid + ")",
                UseShellExecute = false, CreateNoWindow = true
            }))
                return process.WaitForExit(30000) ? process.ExitCode : 3;
        }

        internal void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ManagementServer));
            listener.Start();
            Listen();
        }

        private async void Listen()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException) { return; }
                _ = Handle(context);
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            await requests.WaitAsync().ConfigureAwait(false);
            try
            {
                var request = context.Request;
                var response = context.Response;
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.Headers["X-Frame-Options"] = "DENY";
                response.Headers["Referrer-Policy"] = "no-referrer";
                response.Headers["Cache-Control"] = "no-store";
                if (!AuthorizedHost(request)) { await Json(context, new { error = "仅允许本机访问。" }, 403); return; }
                string path = request.Url.AbsolutePath;
                if (!path.StartsWith("/api/", StringComparison.Ordinal))
                {
                    if (request.HttpMethod != "GET") { await Json(context, new { error = "方法不支持。" }, 405); return; }
                    response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; object-src 'none'";
                    await Static(context, path); return;
                }
                if (!FixedEquals(request.Headers["Authorization"], "Bearer " + token))
                { await Json(context, new { error = "管理页凭据已失效，请从托盘重新打开。" }, 401); return; }
                string origin = request.Headers["Origin"];
                if (origin != null && origin != Address.TrimEnd('/')) { await Json(context, new { error = "请求来源不允许。" }, 403); return; }
                if (request.HttpMethod != "GET" && request.HttpMethod != "POST") { await Json(context, new { error = "方法不支持。" }, 405); return; }
                Dictionary<string, object> body = new Dictionary<string, object>();
                if (request.HttpMethod == "POST")
                {
                    long limit = path.StartsWith("/api/backups/", StringComparison.Ordinal) ? 180L * 1024 * 1024 : 16L * 1024 * 1024;
                    if (request.ContentLength64 < 0 || request.ContentLength64 > limit) throw new ArgumentException("上传内容过大或未指定长度。");
                    if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请求必须使用 JSON。");
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    {
                        string json = await reader.ReadToEndAsync().ConfigureAwait(false);
                        try { body = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = (int)limit }
                            .Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
                        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                        { throw new ArgumentException("请求 JSON 格式无效。", ex); }
                    }
                }
                if (request.HttpMethod == "GET")
                {
                    var q = request.QueryString;
                    int page = int.TryParse(q["page"], out int number) ? number : 1;
                    object result;
                    switch (path)
                    {
                        case "/api/dashboard": result = store.Dashboard(); break;
                        case "/api/books": result = store.Books(); break;
                        case "/api/settings": result = store.Settings(); break;
                        case "/api/sessions": result = store.SessionSummary(); break;
                        case "/api/sessions/detail": result = store.RoundDetail(q["id"]); break;
                        case "/api/records": result = store.Records(q["from"], q["to"], q["book"], page, q["kind"]); break;
                        case "/api/reviews/overview": result = store.ReviewOverview(q["book"]); break;
                        case "/api/words": result = store.Words(q["book"], q["q"], q["filter"], page, q["window"]); break;
                        case "/api/words/detail": result = store.WordDetail(q["key"]); break;
                        case "/api/exports": await Download(context, store.ExportRecords(q["from"], q["to"], q["book"], q["kind"]), "study-records.xlsx"); return;
                        case "/api/books/export":
                            await Download(context, ExportBook(q["book"]), "english-words.xlsx"); return;
                        case "/api/backups":
                            string backup = Path.Combine(store.Root, "download-" + Guid.NewGuid().ToString("N") + ".sqlite");
                            try { store.BackupTo(backup); await Download(context, File.ReadAllBytes(backup), "toastfish-backup.sqlite"); }
                            finally { if (File.Exists(backup)) File.Delete(backup); }
                            return;
                        default: await Json(context, new { error = "接口不存在。" }, 404); return;
                    }
                    await Json(context, result); return;
                }
                bool maintenance = path.StartsWith("/api/imports/", StringComparison.Ordinal) ||
                    path.StartsWith("/api/backups/", StringComparison.Ordinal) || path == "/api/books/reset";
                object changed;
                if (maintenance)
                {
                    OnUi(() => { coordinator.BeginMaintenance(); return null; });
                    try { changed = await Task.Run(() => Mutate(path, body)).ConfigureAwait(false); }
                    finally { OnUi(() => { coordinator.EndMaintenance(); return null; }); }
                }
                else changed = OnUi(() =>
                {
                    if (coordinator.Maintenance) throw new InvalidOperationException("数据维护尚未完成，请稍后重试。");
                    return Mutate(path, body);
                });
                await Json(context, changed);
            }
            catch (Exception ex)
            {
                if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                int status = ex is ArgumentException || ex is FormatException || ex is InvalidDataException ? 400 : ex is InvalidOperationException ? 409 : 500;
                if (status == 500)
                    lock (store.Gate) File.AppendAllText(Path.Combine(store.Root, "management-errors.log"), DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
                try { await Json(context, new { error = status == 500 ? "处理失败，请查看用户数据目录中的 management-errors.log。" : ex.Message }, status); }
                catch (Exception) { }
            }
            finally { try { context.Response.Close(); } finally { requests.Release(); } }
        }

        private object Mutate(string path, Dictionary<string, object> body)
        {
            string Value(string name) => body.TryGetValue(name, out object value) ? Convert.ToString(value) : "";
            int Integer(string name) => int.TryParse(Value(name), out int number) ? number : throw new ArgumentException("缺少或无效的数值：" + name);
            bool Boolean(string name) => bool.TryParse(Value(name), out bool value) ? value : throw new ArgumentException("缺少或无效的布尔值：" + name);
            byte[] FileBytes() => Convert.FromBase64String(Value("data"));
            switch (path)
            {
                case "/api/sessions/start":
                    var selected = body.TryGetValue("keys", out object keys) && keys is System.Collections.IEnumerable array && !(keys is string) ?
                        array.Cast<object>().Select(v => v as string ?? throw new ArgumentException("单词标识必须是字符串。")).ToArray() : null;
                    coordinator.Start(Value("bookId"), Integer("number"), Value("mode"), selected); break;
                case "/api/sessions/resume": coordinator.Resume(); break;
                case "/api/sessions/pause": coordinator.Pause(); break;
                case "/api/sessions/end": coordinator.End(); break;
                case "/api/words/star": store.Star(Value("key"), Boolean("starred")); break;
                case "/api/settings": coordinator.RequireIdle(); store.SaveSettings(StudyJson.Read<ManagementSettings>(StudyJson.Write(body))); break;
                case "/api/books/reset": store.Reset(Value("bookId")); break;
                case "/api/imports/preview":
                    var words = store.PreviewEnglish(FileBytes(), Value("filename"));
                    return new { count = words.Count, items = words.Take(50).ToArray() };
                case "/api/imports/confirm":
                    return new { bookId = store.ImportEnglish(Value("name"), FileBytes(), Value("filename")) };
                case "/api/backups/restore": store.RestoreBackup(FileBytes()); break;
                case "/api/backups/import-legacy": store.RestoreBackup(FileBytes(), true); break;
                default: throw new ArgumentException("接口不存在。");
            }
            return new { ok = true, session = store.SessionSummary() };
        }

        private byte[] ExportBook(string book)
        {
            string file = Path.Combine(store.Root, "export-" + Guid.NewGuid().ToString("N") + ".xlsx");
            var words = store.GetWords(book);
            if (words.Count == 0) throw new ArgumentException("词库不存在。");
            try { new ToastFish.Model.Log.CreateLog().OutputExcel(file, words.Select(w => w.Word).ToList(), "英语"); return File.ReadAllBytes(file); }
            finally { if (File.Exists(file)) File.Delete(file); }
        }

        private static object OnUi(Func<object> action) => Application.Current.Dispatcher.Invoke(action);
        internal static bool FixedEquals(string left, string right)
        {
            if (left == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
        private static bool AuthorizedHost(HttpListenerRequest request) => IPAddress.IsLoopback(request.RemoteEndPoint.Address) &&
            request.Headers["Host"] == "127.0.0.1:17653";

        private async Task Static(HttpListenerContext context, string path)
        {
            string file;
            if (path == "/" || new[] { "/records", "/words", "/reviews", "/settings" }.Contains(path)) file = Path.Combine(webRoot, "index.html");
            else if (path.StartsWith("/assets/", StringComparison.Ordinal) &&
                System.Text.RegularExpressions.Regex.IsMatch(path, @"^/assets/[A-Za-z0-9_-]+\.(js|css|svg|woff2)$"))
                file = Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            else { await Json(context, new { error = "资源不存在。" }, 404); return; }
            if (!File.Exists(file)) { await Json(context, new { error = "资源未构建。" }, 404); return; }
            string extension = Path.GetExtension(file);
            context.Response.ContentType = extension == ".js" ? "text/javascript; charset=utf-8" : extension == ".css" ? "text/css; charset=utf-8" :
                extension == ".svg" ? "image/svg+xml" : extension == ".woff2" ? "font/woff2" : "text/html; charset=utf-8";
            byte[] bytes = File.ReadAllBytes(file);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
        private static Task Json(HttpListenerContext context, object data, int status = 200)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(StudyJson.Write(data));
            context.Response.ContentLength64 = bytes.Length;
            return context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
        private static Task Download(HttpListenerContext context, byte[] bytes, string filename)
        {
            context.Response.ContentType = "application/octet-stream";
            context.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + filename + "\"";
            context.Response.ContentLength64 = bytes.Length;
            return context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
        public void Dispose() { disposed = true; listener.Close(); }
    }
}
