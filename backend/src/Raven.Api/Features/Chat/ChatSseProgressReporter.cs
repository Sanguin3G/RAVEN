using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raven.Api.Features.Chat;

internal sealed class HttpSseChatProgressReporter(HttpResponse response) : IChatProgressReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task ReportAsync(ChatProgressEvent progress, CancellationToken cancellationToken) =>
        WriteAsync("progress", progress, cancellationToken);

    public Task CompleteAsync(SendChatMessageResponse responseBody, CancellationToken cancellationToken) =>
        WriteAsync("completed", responseBody, cancellationToken);

    public Task FailAsync(string code, string message, CancellationToken cancellationToken) =>
        WriteAsync("failed", new ChatStreamFailure(code, message), cancellationToken);

    private async Task WriteAsync<T>(string eventName, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

internal sealed record ChatStreamFailure(string Code, string Message);