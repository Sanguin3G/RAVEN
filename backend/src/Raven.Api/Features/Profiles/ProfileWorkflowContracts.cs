using Raven.Api.Features.Ai;

namespace Raven.Api.Features.Profiles;

public sealed record ProfileGenerationResponse(
    CompanyProfileCandidate? Candidate,
    IReadOnlyList<string> Warnings,
    string Provider,
    string Model,
    string PromptTemplateVersion,
    long DurationMs,
    AiUsage? Usage,
    AiFailure? Failure)
{
    public bool Succeeded => Candidate is not null && Failure is null;
}

public sealed record ConfirmCompanyProfileRequest(Guid CandidateId);

public interface ICompanyProfileWorkflowService
{
    Task<ProfileGenerationResponse?> GenerateAsync(Guid researchRunId, CancellationToken cancellationToken);
    Task<CompanyProfileCandidate?> GetCandidateAsync(Guid researchRunId, CancellationToken cancellationToken);
    Task<CompanyProfileVersion?> ConfirmAsync(Guid researchRunId, Guid candidateId, CancellationToken cancellationToken);
    Task<CompanyProfileVersion?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyProfileVersion>> ListVersionsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyProfileVersion?> GetVersionAsync(Guid companyId, int version, CancellationToken cancellationToken);
}
