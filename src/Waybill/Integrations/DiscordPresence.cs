using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;

namespace Waybill.Integrations;

/// <summary>
/// Discord Rich Presence: the line under the player's name saying what they are
/// driving right now.
///
/// It talks to the Discord client running on the same machine over a local named
/// pipe (<c>\\.\pipe\discord-ipc-N</c>), so nothing here reaches the internet and
/// no account is involved. Discord not being open simply means the pipe never
/// opens and the whole thing sits idle - it can never keep the app from working.
///
/// The protocol is four bytes of opcode, four bytes of payload length and then
/// JSON. Opcode 0 is the handshake, 1 a command or its reply, 2 a close, 3 a ping
/// and 4 a pong.
/// </summary>
public sealed class DiscordPresence : IDisposable {
    /// <summary>What to show. Everything is optional except the text lines, which
    /// Discord truncates past 128 characters anyway.</summary>
    public sealed class Activity {
        public string Details = "";
        public string State = "";
        /// <summary>Unix seconds the drive began, which Discord turns into a live
        /// "elapsed" counter of its own.</summary>
        public long? StartUnix;
        public string? LargeImage;
        public string? LargeText;
        public string? SmallImage;
        public string? SmallText;

        /// <summary>Everything that would be sent, as one string. Used to skip
        /// sending an update that would change nothing.</summary>
        public string Key => string.Join("|", Details, State, StartUnix, LargeImage, LargeText, SmallImage, SmallText);
    }

    // Discord accepts one activity update every 15 seconds and quietly drops the
    // rest, so there is nothing to gain from sending more often.
    private static readonly TimeSpan SendEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(20);
    // Generous: a busy Discord answering slowly must not read as a refusal.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private const int PipesToTry = 10;
    private const int MaxTextLength = 128;

    private readonly string _appId;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;

    private Activity? _desired;
    private string _sentKey = "";
    private NamedPipeClientStream? _pipe;
    private volatile bool _broken;
    private DateTime _lastSend = DateTime.MinValue;
    private DateTime _lastTry = DateTime.MinValue;
    // Said once, not every twenty seconds. Silence was the worst part of this:
    // with nothing to show on the profile and nothing in the log, there was no way
    // to tell a wrong application ID from Discord simply not being open.
    private bool _saidUnreachable;

    /// <summary>Log line worth showing the user, in whatever language is active.</summary>
    public event Action<string>? Message;

    public bool Connected => _pipe != null && !_broken;

    /// <summary>Whether the last attempt found a Discord pipe at all. Separates
    /// "Discord is not running" from "Discord is running and would not have us".</summary>
    public bool SawPipe { get; private set; }

    public DiscordPresence(string appId) {
        _appId = appId;
        // A background thread rather than the UI timer: connecting to the pipe can
        // block for a moment, and the window must never wait on Discord.
        _worker = new Thread(Run) { Name = "waybill-discord", IsBackground = true };
        _worker.Start();
    }

    /// <summary>Set what should be shown, or null to show nothing at all. Returns
    /// immediately; the actual sending happens on the worker thread.</summary>
    public void Update(Activity? activity) {
        lock (_gate) _desired = activity;
    }

    private void Run() {
        while (!_stop.IsCancellationRequested) {
            try {
                Step();
            } catch {
                // Discord closing mid-write lands here. Drop the pipe and let the
                // next pass reconnect; a presence update is never worth a crash.
                Drop();
            }
            try {
                _stop.Token.WaitHandle.WaitOne(1000);
            } catch (ObjectDisposedException) {
                // Dispose won the race for the token; there is nothing left to do.
                return;
            }
        }
    }

    private void Step() {
        Activity? want;
        lock (_gate) want = _desired;

        if (_broken) Drop();

        if (_pipe == null) {
            // Nothing to show means no reason to hold a connection open, and no
            // reason to keep knocking on a pipe that may not exist.
            if (want == null) return;
            if (DateTime.UtcNow - _lastTry < RetryEvery) return;
            _lastTry = DateTime.UtcNow;
            if (!Connect()) {
                if (!_saidUnreachable) {
                    _saidUnreachable = true;
                    Message?.Invoke(Strings.T(SawPipe ? "discord.refused" : "discord.unreachable"));
                }
                return;
            }
            _saidUnreachable = false;
        }

        var key = want?.Key ?? "";
        if (key == _sentKey) return;
        if (DateTime.UtcNow - _lastSend < SendEvery) return;

        Send(want);
        _sentKey = key;
        _lastSend = DateTime.UtcNow;
    }

    /// <summary>Discord numbers its pipes when several clients (stable, PTB, canary)
    /// run at once, so all ten are tried before giving up.</summary>
    private bool Connect() {
        SawPipe = false;
        for (var i = 0; i < PipesToTry; i++) {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try {
                pipe.Connect(200);
            } catch {
                pipe.Dispose();
                continue;
            }

            // Discord is there. Whatever goes wrong from here is about this
            // application rather than about Discord being closed, and the two
            // deserve different things said about them.
            SawPipe = true;
            var attempt = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            try {
                WriteFrame(pipe, 0, JsonConvert.SerializeObject(new { v = 1, client_id = _appId }));
                // A wrong application ID gets the connection closed instead of a
                // READY, which shows up here as no readable reply.
                if (ReadFrame(pipe, HandshakeTimeout, attempt.Token) == null) throw new IOException("no handshake reply");

                _pipe = pipe;
                _broken = false;
                _sentKey = "";
                _lastSend = DateTime.MinValue;
                Drain(pipe);
                Message?.Invoke(Strings.T("discord.connected"));
                return true;
            } catch {
                // Cancel first: a read abandoned on timeout is still outstanding on
                // the handle, and closing a pipe with one pending waits for it.
                attempt.Cancel();
                pipe.Dispose();
            } finally {
                attempt.Dispose();
            }
        }
        return false;
    }

    /// <summary>Every command gets a reply and Discord pings periodically. Nobody
    /// reads either of them for their content, but they have to be taken off the
    /// pipe: left there they eventually fill the buffer and block writing.</summary>
    private void Drain(NamedPipeClientStream pipe) {
        Task.Run(() => {
            try {
                while (pipe.IsConnected && !_stop.IsCancellationRequested) {
                    var frame = ReadFrame(pipe, Timeout.InfiniteTimeSpan, _stop.Token);
                    if (frame == null) break;
                    // A ping unanswered for long enough is treated as a dead client.
                    if (frame.Value.Op == 3) lock (_writeGate) WriteFrame(pipe, 4, frame.Value.Body);
                }
            } catch {
                // Discord quitting closes the pipe under the read, which is normal.
            }
            _broken = true;
        });
    }

    private void Send(Activity? a) {
        object? activity = a == null ? null : new {
            details = Clip(a.Details),
            state = Clip(a.State),
            timestamps = a.StartUnix is { } start ? new { start } : null,
            assets = new {
                large_image = a.LargeImage,
                large_text = Clip(a.LargeText),
                small_image = a.SmallImage,
                small_text = Clip(a.SmallText),
            },
        };

        // Nulls are dropped rather than sent: an absent activity is how Discord is
        // told to clear the presence, and absent asset keys are how a missing
        // image is left out instead of asking for one that was never uploaded.
        var payload = JsonConvert.SerializeObject(new {
            cmd = "SET_ACTIVITY",
            nonce = Guid.NewGuid().ToString(),
            args = new { pid = Environment.ProcessId, activity },
        }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

        lock (_writeGate) WriteFrame(_pipe!, 1, payload);
    }

    private static string? Clip(string? text) {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length <= MaxTextLength ? text : text[..MaxTextLength];
    }

    private void Drop() {
        try {
            _pipe?.Dispose();
        } catch {
            // Already gone, which is the usual reason for being here.
        }
        _pipe = null;
        _broken = false;
        _sentKey = "";
    }

    // ---------- framing ----------

    private static void WriteFrame(Stream stream, int op, string payload) {
        var body = Encoding.UTF8.GetBytes(payload);
        var frame = new byte[8 + body.Length];
        // The header is little endian, which is what BitConverter writes on every
        // platform this runs on.
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), op);
        BitConverter.TryWriteBytes(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame, 8);
        stream.Write(frame, 0, frame.Length);
        stream.Flush();
    }

    private static (int Op, string Body)? ReadFrame(Stream stream, TimeSpan timeout, CancellationToken cancel) {
        var header = new byte[8];
        if (!Fill(stream, header, timeout, cancel)) return null;

        var op = BitConverter.ToInt32(header, 0);
        var length = BitConverter.ToInt32(header, 4);
        // A length this side of sane keeps a confused pipe from asking for a
        // gigabyte-sized buffer.
        if (length < 0 || length > 1 << 20) return null;

        var body = new byte[length];
        if (length > 0 && !Fill(stream, body, timeout, cancel)) return null;
        return (op, Encoding.UTF8.GetString(body));
    }

    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes, or gives up.
    /// Reads are asynchronous so that waiting for a reply that never comes cannot
    /// wedge the thread forever, and they take the token so that closing the app
    /// cancels the read in flight. Without that, disposing the pipe waits on the
    /// outstanding read and the whole window hangs on the way out.</summary>
    private static bool Fill(Stream stream, byte[] buffer, TimeSpan timeout, CancellationToken cancel) {
        var infinite = timeout == Timeout.InfiniteTimeSpan;
        var deadline = infinite ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        var read = 0;

        while (read < buffer.Length) {
            var task = stream.ReadAsync(buffer, read, buffer.Length - read, cancel);
            if (infinite) {
                task.Wait();
            } else {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !task.Wait(left)) return false;
            }

            var got = task.Result;
            if (got <= 0) return false;
            read += got;
        }
        return true;
    }

    public void Dispose() {
        try {
            // Cleared before the cancellation, not after: cancelling aborts the read
            // the drain loop is sitting in, which marks the connection broken, and a
            // broken connection has nothing left to clear with. Leaving a stale
            // "driving to Praha" on the profile is worse than never showing anything.
            if (_pipe != null && !_broken) Send(null);
        } catch {
            // Discord is already gone, so the profile has cleared itself.
        }

        _stop.Cancel();
        _worker.Join(TimeSpan.FromSeconds(1));
        Drop();
        _stop.Dispose();
    }
}
