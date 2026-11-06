using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ShadBuffer
{
    public enum UploadState
    {
        Idle,
        Uploading,
        Done,
        Deleting,
        Failed,
    }

    public sealed class UploadJob
    {
        public string Path { get; set; }
        public UploadState State { get; set; }
        public long Sent { get; set; }
        public long Total { get; set; }
        public string Link { get; set; }
        public string Key { get; set; }
        public string Error { get; set; }
        internal CancellationTokenSource Cancel { get; set; }
    }

    // lives in the tray helper so an upload keeps going after the overlay closes
    public sealed class Uploader
    {
        // kappa.lol's front page says "Max file size: 100 MiB"
        public const long MaxBytes = 100L * 1024 * 1024;

        private const int ChunkBytes = 64 * 1024;
        private const int RequestTimeoutMs = 5 * 60 * 1000;
        private const int ReadWriteTimeoutMs = 2 * 60 * 1000;
        private const int MaxErrorLength = 120;
        private const string ConnectionError = "Upload failed. Check your connection and try again.";
        private const string ServerError = "Upload failed. kappa.lol isn't taking uploads right now, try again later.";
        private const string MissingFileError = "Upload failed. That file isn't there anymore.";
        private const string TooBigError = "Upload failed. That file is too big for kappa.lol.";
        private const string TooManyError = "Upload failed. kappa.lol says that's too many uploads, wait a bit and try again.";
        private const string DeleteError = "Couldn't delete it, try again in a bit. The link still works.";
        private const int FirstServerErrorCode = 500;
        private const int NotFoundCode = 404;
        private const int TooBigCode = 413;
        private const int TooManyCode = 429;

        private static readonly TimeSpan NotifyEvery = TimeSpan.FromMilliseconds(150);

        private static string apiUrl = "https://kappa.lol/api/";

        private readonly Context ctx;
        private readonly SynchronizationContext ui = SynchronizationContext.Current;
        private readonly Dictionary<string, UploadJob> jobs = new Dictionary<string, UploadJob>();

        public event Action<UploadJob> Changed;

        public Uploader(Context ctx)
        {
            this.ctx = ctx;
            LoadSaved();
        }

        public UploadJob Get(string path)
        {
            return jobs.TryGetValue(path, out UploadJob job) ? job : null;
        }

        public void Start(string path, long size)
        {
            var job = new UploadJob
            {
                Path = path,
                State = UploadState.Uploading,
                Total = size,
                Cancel = new CancellationTokenSource(),
            };
            jobs[path] = job;
            Task.Run(() => Run(job));
        }

        public void Cancel(string path)
        {
            if (jobs.TryGetValue(path, out UploadJob job) && job.State == UploadState.Uploading)
            {
                job.Cancel.Cancel();
            }
        }

        public void Delete(string path)
        {
            if (jobs.TryGetValue(path, out UploadJob job) && job.State == UploadState.Done && !string.IsNullOrEmpty(job.Key))
            {
                job.Error = null;
                job.State = UploadState.Deleting;
                Task.Run(() => RunDelete(job));
            }
        }

        private void Run(UploadJob job)
        {
            try
            {
                Send(job);
                job.State = UploadState.Done;
            }
            catch (Exception) when (job.Cancel.IsCancellationRequested)
            {
                job.State = UploadState.Idle;
            }
            catch (UploadRefused e)
            {
                ctx.Log("Upload refused: " + e.Message);
                job.Error = e.Message;
                job.State = UploadState.Failed;
            }
            catch (Exception e)
            {
                ctx.Log("Upload failed: " + e.Message);
                job.Error = FailureText(job, e);
                job.State = UploadState.Failed;
            }
            Finish(job);
        }

        // when the server hangs up on a finished upload, the connection on this end was fine
        private static string FailureText(UploadJob job, Exception failure)
        {
            if (!File.Exists(job.Path))
            {
                return MissingFileError;
            }
            bool reachedServer = failure is WebException web
                && (web.Status == WebExceptionStatus.ConnectionClosed || (job.Sent >= job.Total && web.Status != WebExceptionStatus.Timeout));
            return reachedServer ? ServerError : ConnectionError;
        }

        private void Send(UploadJob job)
        {
            UseModernTls();

            string boundary = "shadbuffer" + Guid.NewGuid().ToString("N");
            // the host keeps the upload's name, and mine has the game in it, so this one stays generic
            string extension = Path.GetExtension(job.Path);
            string fileName = "clip" + extension;
            byte[] head = Encoding.ASCII.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {ContentType(extension)}\r\n\r\n");
            byte[] tail = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");

            using (var file = new FileStream(job.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                job.Total = file.Length;
                HttpWebRequest request = NewRequest("upload");
                request.Method = "POST";
                request.ContentType = "multipart/form-data; boundary=" + boundary;
                request.ContentLength = head.Length + file.Length + tail.Length;
                request.AllowWriteStreamBuffering = false;
                request.KeepAlive = false;

                using (job.Cancel.Token.Register(request.Abort))
                {
                    using (Stream body = request.GetRequestStream())
                    {
                        body.Write(head, 0, head.Length);
                        var chunk = new byte[ChunkBytes];
                        DateTime nextNotify = DateTime.UtcNow;
                        int read;
                        while ((read = file.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            job.Cancel.Token.ThrowIfCancellationRequested();
                            body.Write(chunk, 0, read);
                            job.Sent += read;
                            if (DateTime.UtcNow >= nextNotify)
                            {
                                nextNotify = DateTime.UtcNow + NotifyEvery;
                                Post(job);
                            }
                        }
                        body.Write(tail, 0, tail.Length);
                    }
                    ReadUploadReply(request, job);
                }
            }
        }

        private static void ReadUploadReply(HttpWebRequest request, UploadJob job)
        {
            string text;
            try
            {
                text = ReadText(request);
            }
            catch (WebException e) when (e.Response is HttpWebResponse response)
            {
                int code = (int)response.StatusCode;
                if (code >= FirstServerErrorCode)
                {
                    throw new UploadRefused(ServerError);
                }
                if (code == TooBigCode)
                {
                    throw new UploadRefused(TooBigError);
                }
                if (code == TooManyCode)
                {
                    throw new UploadRefused(TooManyError);
                }
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    throw new UploadRefused("Upload failed: " + Explain(reader.ReadToEnd().Trim()));
                }
            }

            Dictionary<string, object> reply = ParseJson(text);
            string link = Value(reply, "link");
            if (link == null || !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new UploadRefused("Upload failed: " + Explain(text));
            }
            job.Link = link;
            job.Key = Value(reply, "key");
        }

        private void RunDelete(UploadJob job)
        {
            try
            {
                UseModernTls();
                HttpWebRequest request = NewRequest("delete?key=" + Uri.EscapeDataString(job.Key));
                Dictionary<string, object> reply = ParseJson(ReadText(request));
                if (reply == null || !reply.TryGetValue("success", out object success) || !(success is bool done) || !done)
                {
                    throw new UploadRefused(DeleteError);
                }
                job.State = UploadState.Idle;
            }
            catch (WebException e) when (e.Response is HttpWebResponse response && (int)response.StatusCode == NotFoundCode)
            {
                // already gone from the host, so there's nothing left to delete
                job.State = UploadState.Idle;
            }
            catch (Exception e)
            {
                ctx.Log("Delete failed: " + e.Message);
                job.Error = DeleteError;
                job.State = UploadState.Done;
            }
            Finish(job);
        }

        private static void UseModernTls()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
        }

        private static HttpWebRequest NewRequest(string route)
        {
            var request = (HttpWebRequest)WebRequest.Create(apiUrl + route);
            request.Timeout = RequestTimeoutMs;
            request.ReadWriteTimeout = ReadWriteTimeoutMs;
            request.UserAgent = Context.AppName + "/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            return request;
        }

        private static string ReadText(HttpWebRequest request)
        {
            using (WebResponse response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd().Trim();
            }
        }

        private static Dictionary<string, object> ParseJson(string text)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string Value(Dictionary<string, object> reply, string name)
        {
            return reply != null && reply.TryGetValue(name, out object value) ? value as string : null;
        }

        private static string ContentType(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".mp4":
                    return "video/mp4";
                case ".webm":
                    return "video/webm";
                case ".mov":
                    return "video/quicktime";
                case ".mkv":
                    return "video/x-matroska";
                default:
                    return "application/octet-stream";
            }
        }

        // an error page or raw json isn't anything a person wants to read
        private static string Explain(string text)
        {
            Dictionary<string, object> reply = ParseJson(text);
            string said = Value(reply, "error") ?? Value(reply, "message");
            if (said != null)
            {
                text = said;
            }
            else if (text.Length == 0 || text.StartsWith("<") || text.StartsWith("{"))
            {
                return "the server didn't say why.";
            }
            return text.Length <= MaxErrorLength ? text : text.Substring(0, MaxErrorLength);
        }

        // files have no set expiry there, so the delete link has to outlive a restart
        private void LoadSaved()
        {
            if (!File.Exists(ctx.UploadsPath))
            {
                return;
            }
            try
            {
                foreach (SavedUpload saved in new JavaScriptSerializer().Deserialize<List<SavedUpload>>(File.ReadAllText(ctx.UploadsPath)))
                {
                    jobs[saved.Path] = new UploadJob { Path = saved.Path, State = UploadState.Done, Link = saved.Link, Key = saved.Key };
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException)
            {
                ctx.Log("Couldn't read saved uploads: " + e.Message);
            }
        }

        private void SaveAll()
        {
            List<SavedUpload> saved = jobs.Values
                .Where(job => job.State == UploadState.Done || job.State == UploadState.Deleting)
                .Select(job => new SavedUpload { Path = job.Path, Link = job.Link, Key = job.Key })
                .ToList();
            try
            {
                Directory.CreateDirectory(ctx.AppDir);
                File.WriteAllText(ctx.UploadsPath, new JavaScriptSerializer().Serialize(saved));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ctx.Log("Couldn't save uploads: " + e.Message);
            }
        }

        private void Post(UploadJob job)
        {
            ui.Post(_ => Changed?.Invoke(job), null);
        }

        // jobs is only ever touched on the ui thread, so all of this happens over there
        private void Finish(UploadJob job)
        {
            ui.Post(_ =>
            {
                if (job.State == UploadState.Idle)
                {
                    jobs.Remove(job.Path);
                }
                SaveAll();
                Changed?.Invoke(job);
            }, null);
        }

        private sealed class UploadRefused : Exception
        {
            public UploadRefused(string message) : base(message)
            {
            }
        }

        private sealed class SavedUpload
        {
            public string Path { get; set; }
            public string Link { get; set; }
            public string Key { get; set; }
        }
    }
}
