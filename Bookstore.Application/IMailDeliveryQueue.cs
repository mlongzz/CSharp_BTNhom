namespace Bookstore.Application;

public sealed record OutboxMessage(long Id, string Recipient, string Subject, string Message, string Lease);

public interface IMailDeliveryQueue
{
    OutboxMessage? Claim(DateTimeOffset now);
    void Finish(OutboxMessage message, DateTimeOffset now, bool delivered);
}

