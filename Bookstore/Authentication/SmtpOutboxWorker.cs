using System.Net;
using System.Net.Mail;
using Bookstore.Application;

namespace Bookstore.Authentication;

internal sealed record SmtpSettings(string Host, int Port, MailAddress Sender,
    string? Username, string? Password, bool EnableSsl)
{
    public static SmtpSettings? From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Bookstore:Smtp");
        var host = section["Host"];
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var port = section.GetValue("Port", 587);
        if (port is < 1 or > 65535 || !MailAddress.TryCreate(section["Sender"], out var sender))
            throw new InvalidOperationException("Bookstore:Smtp requires a valid Sender and Port.");

        return new(host, port, sender, section["Username"], section["Password"], section.GetValue("EnableSsl", true));
    }
}

internal sealed class SmtpOutboxWorker(IServiceScopeFactory scopes, SmtpSettings settings, TimeProvider clock,
    ILogger<SmtpOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DeliverOne(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("SMTP outbox processing failed ({ErrorType})", exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task<bool> DeliverOne(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IMailDeliveryQueue>();
        var entry = queue.Claim(clock.GetUtcNow());
        if (entry is null)
            return false;

        var delivered = false;
        try
        {
            using var message = new MailMessage(settings.Sender, new MailAddress(entry.Recipient))
            {
                Subject = entry.Subject,
                Body = entry.Message
            };
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.EnableSsl,
                Credentials = settings.Username is null
                    ? null
                    : new NetworkCredential(settings.Username, settings.Password)
            };
            await client.SendMailAsync(message, stoppingToken);
            delivered = true;
        }
        catch (Exception exception) when (exception is SmtpException or FormatException)
        {
            logger.LogWarning("SMTP delivery failed for outbox message {MessageId} ({ErrorType})",
                entry.Id, exception.GetType().Name);
        }

        queue.Finish(entry, clock.GetUtcNow(), delivered);
        return true;
    }
}

