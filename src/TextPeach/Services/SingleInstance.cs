using System.IO.Pipes;
using System.Text;

namespace TextPeach.Services;

/// <summary>
/// One TextPeach per user session. A second launch (e.g. "TextPeach.exe &lt;carpeta&gt;" from another app)
/// hands its arguments to the running instance over a named pipe and exits, so both never fight over state.json.
/// </summary>
public static class SingleInstance
{
    private static readonly string Id = "TextPeach-" + Environment.UserName;
    private static Mutex? _mutex;

    /// <summary>Raised on a background thread with the (absolute) paths another launch asked to open.</summary>
    public static event Action<string[]>? Received;

    /// <summary>True when this process is the primary instance; otherwise the arguments were forwarded.</summary>
    public static bool TryBecomePrimary(string[] args)
    {
        _mutex = new Mutex(true, @"Local\" + Id, out bool created);
        if (created)
        {
            new Thread(Listen) { IsBackground = true, Name = "SingleInstance" }.Start();
            return true;
        }
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(3000);
            var payload = string.Join('\n', args.Select(a => { try { return Path.GetFullPath(a); } catch { return a; } }));
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes);
            return false;
        }
        catch
        {
            // The other instance is not answering (starting up or hung): run on our own instead of doing nothing.
            return true;
        }
    }

    private static void Listen()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = reader.ReadToEnd();
                var args = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Received?.Invoke(args);
            }
            catch { Thread.Sleep(200); }
        }
    }
}
