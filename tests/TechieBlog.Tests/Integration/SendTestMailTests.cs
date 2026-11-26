using System.Net;
using System.Net.Sockets;
using BlogEngine.Services;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using TechieBlog.Tests.Engagement;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// [REQ-UI-072] "Send test mail" on the Settings screen sends a real mail with the values on the screen
/// and reports the mail server's real answer, good or bad.
/// </summary>
public sealed class SendTestMailTests
{
    /// <summary>
    /// A mail server that accepts: the mail arrives and the answer says it was accepted.
    /// </summary>
    [Fact]
    public async Task AcceptedTestMailArrivesAndSaysSo()
    {
        await using var catcher = MailCatcher.Start();

        var outcome = await Tester().SendTestMailAsync(Server(catcher.Port), "owner@journey.test");
        await catcher.WaitForAsync("owner@journey.test");

        Assert.True(outcome.IsAccepted, outcome.ServerAnswer);
    }

    /// <summary>
    /// A mail server that refuses the address: the answer carries the server's own reply.
    /// </summary>
    [Fact]
    public async Task RefusedTestMailShowsTheServersReply()
    {
        await using var catcher = MailCatcher.Start();
        catcher.RejectRecipientsWith = "550 5.1.1 Mailbox unavailable";

        var outcome = await Tester().SendTestMailAsync(Server(catcher.Port), "owner@journey.test");

        Assert.Contains("Mailbox unavailable", outcome.ServerAnswer);
    }

    /// <summary>
    /// Nothing listening on the port: the answer says the connection failed, not "accepted".
    /// </summary>
    [Fact]
    public async Task NoServerListeningIsReportedAsRefused()
    {
        var outcome = await Tester().SendTestMailAsync(Server(FreePort()), "owner@journey.test");

        Assert.False(outcome.IsAccepted);
    }

    /// <summary>
    /// A port that takes the connection and never answers — what a firewall that drops outgoing mail
    /// looks like — ends within the send time limit with "No answer", instead of hanging forever.
    /// Measured 2026-11-26: before the bounded wait, the Settings screen spun with no end.
    /// </summary>
    [Fact]
    public async Task SilentPortEndsWithNoAnswerInsteadOfHanging()
    {
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            var port = ((IPEndPoint)silent.LocalEndpoint).Port;
            var outcome = await Tester().SendTestMailAsync(Server(port), "owner@journey.test")
                .WaitAsync(TimeSpan.FromSeconds(60), Xunit.TestContext.Current.CancellationToken);

            Assert.StartsWith("No answer from", outcome.ServerAnswer);
        }
        finally
        {
            silent.Stop();
        }
    }

    /// <summary>
    /// Port 465 is refused with a sentence that says to use 587.
    /// </summary>
    [Fact]
    public async Task ImplicitTlsPortIsExplained()
    {
        var outcome = await Tester().SendTestMailAsync(Server(465), "owner@journey.test");

        Assert.Contains("587", outcome.ServerAnswer);
    }

    /// <summary>
    /// The sender with NOTHING saved on the Settings screen or in configuration, so every test above
    /// also proves the test mail uses the values on the screen, not the saved ones.
    /// </summary>
    private static SmtpEmailService Tester()
    {
        var nothingSaved = new MailServerSource(
            new FakeSiteSettingsService(), new FakeConfiguration(new()), NullLogger<MailServerSource>.Instance);
        return new SmtpEmailService(nothingSaved, NullLogger<SmtpEmailService>.Instance);
    }

    private static SmtpSettings Server(int port)
    {
        return new SmtpSettings { Host = "127.0.0.1", Port = port, IsSslEnabled = false, FromAddress = "blog@journey.test" };
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
