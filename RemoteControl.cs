using System.IO.Pipes;

namespace RGBCommander;

/// <summary>One-way command channel to the running single instance: a second launch
/// (e.g. a Stream Deck button running "Prisma.exe --effect rainbow") forwards its
/// command over a named pipe instead of opening a second window. Carries the data the
/// old show-signal EventWaitHandle could not.</summary>
public static class RemoteControl
{
    private const string PipeName = "RGBCommander_Cmd_v1";
    /// <summary>Tokens are joined with this (ASCII Unit Separator) when forwarded, so
    /// profile names with spaces survive intact as one argument.</summary>
    public const char Sep = (char)0x1F;

    /// <summary>Runs in the first instance: accepts one command per connection and
    /// hands the raw text to <paramref name="onCommand"/> (off the UI thread — the
    /// callback marshals).</summary>
    public static void StartServer(Action<string> onCommand)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string text = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(text)) onCommand(text);
                }
                catch { Thread.Sleep(200); } // a malformed connection must not kill the listener
            }
        }) { IsBackground = true, Name = "PrismaRemote" };
        t.Start();
    }

    /// <summary>Runs in a forwarding launch: sends one command to the running instance.
    /// Returns false if no instance is listening.</summary>
    public static bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(command);
            return true;
        }
        catch { return false; }
    }
}
