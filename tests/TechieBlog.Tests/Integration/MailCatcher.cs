using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// A tiny SMTP server that catches mail instead of delivering it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a test send real SMTP mail through the application's own sender and read
/// what arrived (REQ-NFR-045). It is compiled into the test assembly and writes every caught mail as an
/// <c>.eml</c> file under <c>mail-catcher/</c> in the test project's own <c>bin</c> folder — nothing
/// outside the repository, no package, no container.</para>
/// <para><b>Code Flow:</b> listens on 127.0.0.1 on a free port; speaks just enough SMTP (EHLO/HELO,
/// MAIL, RCPT, DATA, RSET, NOOP, QUIT) with no STARTTLS and no AUTH, so the sender must be set with
/// "Use SSL/TLS" off and no user name.</para>
/// <para><b>Usage:</b> <c>await using var catcher = MailCatcher.Start();</c> then point the mail server
/// at <c>127.0.0.1:catcher.Port</c>. Set <see cref="RejectRecipientsWith"/> to make it refuse every
/// recipient with that SMTP reply, to test how a refusal is reported.</para>
/// </remarks>
public sealed class MailCatcher : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentQueue<CaughtMail> caught = new();
    private readonly string folder;
    private readonly Task acceptLoop;

    private MailCatcher()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        folder = Path.Combine(AppContext.BaseDirectory, "mail-catcher", $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Port}");
        Directory.CreateDirectory(folder);
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Gets the port the catcher listens on.</summary>
    public int Port { get; }

    /// <summary>Gets or sets an SMTP reply to send to every RCPT, such as "550 5.1.1 Mailbox unavailable"; null accepts.</summary>
    public string? RejectRecipientsWith { get; set; }

    /// <summary>Gets every mail caught so far.</summary>
    public IReadOnlyList<CaughtMail> Messages => caught.ToList();

    /// <summary>
    /// Starts a catcher on a free loopback port.
    /// </summary>
    /// <returns>The running catcher.</returns>
    public static MailCatcher Start()
    {
        return new MailCatcher();
    }

    /// <summary>
    /// Waits until a mail to <paramref name="toAddress"/> has arrived.
    /// </summary>
    /// <param name="toAddress">The recipient.</param>
    /// <param name="timeout">How long to wait; ten seconds when omitted.</param>
    /// <returns>The mail.</returns>
    /// <exception cref="TimeoutException">No such mail arrived in time.</exception>
    public async Task<CaughtMail> WaitForAsync(string toAddress, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var mail = caught.FirstOrDefault(m => m.Recipients.Contains(toAddress, StringComparer.OrdinalIgnoreCase));
            if (mail != null)
                return mail;

            await Task.Delay(50);
        }

        throw new TimeoutException($"No mail to {toAddress} reached the catcher.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Stop();
        try
        {
            await acceptLoop;
        }
        catch (Exception)
        {
            // The listener was stopped under the loop; nothing to report.
        }

        stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 mail-catcher ready");

            var recipients = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                    case "HELO":
                        await writer.WriteLineAsync("250 mail-catcher");
                        break;
                    case "MAIL":
                        recipients.Clear();
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "RCPT":
                        if (RejectRecipientsWith != null)
                        {
                            await writer.WriteLineAsync(RejectRecipientsWith);
                            break;
                        }

                        recipients.Add(ExtractAddress(line));
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        Store(await ReadDataAsync(reader), recipients);
                        await writer.WriteLineAsync("250 OK queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
        }
    }

    private static async Task<string> ReadDataAsync(StreamReader reader)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line && line != ".")
            data.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");

        return data.ToString();
    }

    private void Store(string raw, List<string> recipients)
    {
        var mail = new CaughtMail(recipients.ToList(), raw);
        File.WriteAllText(Path.Combine(folder, $"{caught.Count + 1:D3}.eml"), raw);
        caught.Enqueue(mail);
    }

    private static string ExtractAddress(string line)
    {
        var match = Regex.Match(line, "<([^>]*)>");
        return match.Success ? match.Groups[1].Value : line[(line.IndexOf(':') + 1)..].Trim();
    }
}

/// <summary>
/// One mail the <see cref="MailCatcher"/> received.
/// </summary>
/// <param name="Recipients">The RCPT addresses.</param>
/// <param name="Raw">The message as sent, headers and body.</param>
public sealed record CaughtMail(IReadOnlyList<string> Recipients, string Raw)
{
    /// <summary>
    /// Gets the decoded text of every body part, so a link can be read whatever transfer encoding the
    /// sender chose (quoted-printable or base64).
    /// </summary>
    public string DecodedBody => Decode(Raw);

    /// <summary>
    /// Finds the first confirmation link (<c>…/verify/{token}</c>) in the mail.
    /// </summary>
    /// <returns>The link.</returns>
    public string VerificationLink()
    {
        var match = Regex.Match(DecodedBody, @"https?://[^\s""'<>]+/verify/[A-Za-z0-9_\-]+");
        return match.Success ? match.Value : throw new InvalidOperationException("The mail carries no confirmation link.");
    }

    private static string Decode(string raw)
    {
        var headerEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = headerEnd < 0 ? raw : raw[..headerEnd];
        var body = headerEnd < 0 ? string.Empty : raw[(headerEnd + 4)..];

        if (headers.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
        {
            var compact = Regex.Replace(body, @"\s", string.Empty);
            return Encoding.UTF8.GetString(Convert.FromBase64String(compact));
        }

        if (headers.Contains("Content-Transfer-Encoding: quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            var joined = body.Replace("=\r\n", string.Empty);
            return Regex.Replace(joined, "=([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        return body;
    }
}
