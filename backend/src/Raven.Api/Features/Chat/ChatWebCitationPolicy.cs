namespace Raven.Api.Features.Chat;

/// <summary>Web candidate IDs are temporary handles for one agent turn. This prevents a model from attaching a prior turn's source to a new answer.</summary>
public static class ChatWebCitationPolicy
{
    public static IReadOnlyList<string> UnsupportedCurrentTurnIds(
        IEnumerable<string> citedCandidateIds,
        IEnumerable<ChatWebEvidenceDraft> currentTurnEvidence) =>
        citedCandidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Where(id => currentTurnEvidence.All(evidence => !string.Equals(evidence.CandidateId, id, StringComparison.Ordinal)))
            .ToArray();
}