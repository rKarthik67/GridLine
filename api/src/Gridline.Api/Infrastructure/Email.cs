using System.Net;
using System.Net.Mail;

namespace Gridline.Infrastructure;

/// <summary>
/// Sends the few emails the product needs (invitations, password resets, bid links). With Email:Smtp:Host set it
/// sends through SMTP; without it, the message is written to the log so a developer can copy the link. Nothing
/// in the app waits on email: every flow also shows the link to the person who triggered it.
/// </summary>
public interface IEmailSender
{
    bool Configured { get; }
    Task Send(string to, string subject, string text, string? html = null, CancellationToken ct = default);
}

public sealed class EmailSender(IConfiguration cfg, ILogger<EmailSender> log) : IEmailSender
{
    readonly string? _host = cfg["Email:Smtp:Host"];
    readonly int _port = int.TryParse(cfg["Email:Smtp:Port"], out var p) ? p : 587;
    readonly string? _user = cfg["Email:Smtp:User"];
    readonly string? _pass = cfg["Email:Smtp:Password"];
    readonly bool _ssl = !string.Equals(cfg["Email:Smtp:Ssl"], "false", StringComparison.OrdinalIgnoreCase);
    readonly string _from = cfg["Email:From"] is { Length: > 0 } f ? f : "Gridline <no-reply@gridline.local>";

    public bool Configured => !string.IsNullOrWhiteSpace(_host);

    public async Task Send(string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        if (!Configured)
        {
            log.LogInformation("Email (not sent: Email:Smtp:Host is not set) → {To}\n      subject: {Subject}\n      {Text}", to, subject, text.Replace("\n", "\n      "));
            return;
        }
        using var msg = new MailMessage(_from, to, subject, html ?? text) { IsBodyHtml = html is not null };
        if (html is not null) msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, null, "text/plain"));
        using var client = new SmtpClient(_host, _port) { EnableSsl = _ssl, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrEmpty(_user)) client.Credentials = new NetworkCredential(_user, _pass);
        try { await client.SendMailAsync(msg, ct); log.LogInformation("Email sent → {To}: {Subject}", to, subject); }
        catch (Exception ex) { log.LogError(ex, "Email to {To} failed: {Subject}", to, subject); }
    }
}

/// <summary>The link people get back in a reply. Email:PublicUrl is the address users open the app at (behind a proxy the request host is not it).</summary>
public static class PublicUrl
{
    public static string Of(IConfiguration cfg, HttpRequest req, string path)
    {
        var baseUrl = cfg["Email:PublicUrl"] is { Length: > 0 } u ? u.TrimEnd('/') : $"{req.Scheme}://{req.Host}";
        return baseUrl + path;
    }
}
