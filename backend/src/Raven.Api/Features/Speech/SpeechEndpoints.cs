using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;

namespace Raven.Api.Features.Speech;

public static class SpeechEndpoints
{
    public static IEndpointRouteBuilder MapSpeechEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/speech").WithTags("Speech");
        group.MapPost("/live-token", LiveTokenAsync).WithName("CreateSpeechLiveToken")
            .WithSummary("Create a one-use restricted Gemini Live transcription token")
            .Produces<GeminiLiveTokenResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/synthesize", SynthesizeAsync).WithName("SynthesizeSpeech")
            .WithSummary("Generate user-requested speech audio")
            .Produces<GeminiSpeechAudioResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> LiveTokenAsync(GeminiLiveTokenRequest request, GeminiSpeechService service, CancellationToken ct)
    {
        try { return TypedResults.Ok(new GeminiLiveTokenResponse(await service.CreateLiveTokenAsync(request.Language, ct))); }
        catch (GeminiSpeechException exception) { return TypedResults.Problem(exception.Message, statusCode: exception.StatusCode); }
    }

    private static async Task<IResult> SynthesizeAsync(GeminiSpeechRequest request, GeminiSpeechService service, CancellationToken ct)
    {
        try { return TypedResults.Ok(await service.SynthesizeAsync(request.Text, request.Voice, ct)); }
        catch (GeminiSpeechException exception) { return TypedResults.Problem(exception.Message, statusCode: exception.StatusCode); }
    }
}

public sealed record GeminiLiveTokenRequest(string? Language);
