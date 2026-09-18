using Raven.Api.Data;

namespace Raven.Api.Features.Chat;

public interface IChatActivityReporter
{
    Task ReportAsync(Guid assistantMessageId, string activity, CancellationToken cancellationToken);
}

public sealed class ChatActivityReporter(RavenDbContext dbContext) : IChatActivityReporter
{
    public async Task ReportAsync(Guid assistantMessageId, string activity, CancellationToken cancellationToken)
    {
        var message = await dbContext.ChatMessages.FindAsync([assistantMessageId], cancellationToken);
        if (message is null || message.Status != ChatMessageStatus.Pending) return;
        message.Activity = ChatText.Bound(activity, 100);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}