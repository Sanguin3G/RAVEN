namespace Raven.Api.Features.Research.Identity;

/// <summary>RAVEN, rather than Gemini, decides the workflow state.</summary>
public sealed class IdentityResolutionPolicy
{
    public IdentityResolutionResponse Derive(IdentityTopologyResponse topology)
        => Derive(topology, request: null);

    public IdentityResolutionResponse Derive(IdentityTopologyResponse topology, IdentityResolutionRequest? request)
    {
        ArgumentNullException.ThrowIfNull(topology);
        var entities = topology.Entities ?? [];
        var hints = topology.RequestedHints?.Distinct().Take(3).ToArray() ?? [];

        if (topology.Failure is not null)
            return Make(IdentityResolutionStatus.NeedsMoreInfo, IdentityAmbiguityType.Unclear, null, [], Useful(hints), "RAVEN couldn't confidently resolve this organization right now.", topology);

        // Non-negotiable: a model cannot auto-select a parent from a family shorthand.
        if (topology.Interpretation == IdentityQueryInterpretation.CorporateFamilyShorthand)
        {
            var family = entities.Where(x => x.Confidence != IdentityConfidence.Low).ToArray();
            return family.Length >= 2
                ? Make(IdentityResolutionStatus.Ambiguous, IdentityAmbiguityType.CorporateFamily, null, family, Useful(hints), topology.Message ?? "Several organizations could match.", topology)
                : Make(IdentityResolutionStatus.NeedsMoreInfo, IdentityAmbiguityType.Unclear, null, [], Useful(hints), "A little more information will help.", topology);
        }

        if (topology.Interpretation == IdentityQueryInterpretation.NameCollision)
        {
            var matches = entities.Where(x => x.Confidence != IdentityConfidence.Low).ToArray();
            return matches.Length >= 2
                ? Make(IdentityResolutionStatus.Ambiguous, IdentityAmbiguityType.NameCollision, null, matches, hints, topology.Message ?? "Several organizations could match.", topology)
                : Make(IdentityResolutionStatus.NeedsMoreInfo, IdentityAmbiguityType.NameCollision, null, [], Useful(hints), "Country or website would usually be enough.", topology);
        }

        if (topology.Interpretation == IdentityQueryInterpretation.SpecificEntity)
        {
            var exact = entities.Where(x => x.RelationshipToQuery is IdentityRelationshipToQuery.Exact or IdentityRelationshipToQuery.Alias && x.Confidence == IdentityConfidence.High).ToArray();
            // A short, generic input is not permission to turn a parent group
            // into the user's intended target. This is a deterministic safety
            // net for a topology model that ignores the family-shorthand rule.
            if (exact.Length == 1 && exact[0].EntityType == IdentityEntityType.ParentGroup && IsGenericShortName(request?.Name))
            {
                return Make(IdentityResolutionStatus.NeedsMoreInfo, IdentityAmbiguityType.Unclear, null, [], Useful(hints),
                    "This name may refer to a group or one of its companies. Country or website would usually be enough.", topology);
            }
            return exact.Length == 1
                ? Make(IdentityResolutionStatus.Resolved, IdentityAmbiguityType.None, exact[0].TemporaryId, exact, [], topology.Message, topology)
                : Make(IdentityResolutionStatus.NeedsMoreInfo, IdentityAmbiguityType.Unclear, null, [], Useful(hints), "A little more information will help.", topology);
        }

        return Make(IdentityResolutionStatus.Unknown, IdentityAmbiguityType.Unclear, null, [], Useful(hints), topology.Message ?? "RAVEN doesn't recognize this organization confidently yet.", topology);
    }

    private static IdentityResolutionResponse Make(IdentityResolutionStatus status, IdentityAmbiguityType type, string? id, IReadOnlyList<IdentityOption> entities, IReadOnlyList<IdentityHintKind> hints, string? message, IdentityTopologyResponse topology) =>
        new(status, type, id, entities, hints, message, IdentityResolutionMethod.ModelKnowledge, topology.ModelUsed, topology.Warning);

    private static IReadOnlyList<IdentityHintKind> Useful(IReadOnlyList<IdentityHintKind> hints) => hints.Count > 0 ? hints : [IdentityHintKind.Country, IdentityHintKind.Website];

    private static bool IsGenericShortName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1;
}
