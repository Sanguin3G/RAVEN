using System.Text.Json;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research.Coverage;

/// <summary>
/// Evaluates whether acquired evidence is useful for the dossier targets. This
/// is deliberately a qualitative policy: a source count is diagnostic, while
/// authority, source purpose, and same-entity status determine the level.
/// </summary>
public sealed class EvidenceCoverageEvaluator : IEvidenceCoverageEvaluator
{
    private static readonly ResearchTarget[] DefaultTargets = Enum.GetValues<ResearchTarget>();

    private readonly ISourceAuthorityPolicy authorityPolicy;

    public EvidenceCoverageEvaluator(ISourceAuthorityPolicy? authorityPolicy = null)
    {
        this.authorityPolicy = authorityPolicy ?? new SourceAuthorityPolicy();
    }

    public EvidenceCoverageResponse Evaluate(
        Guid companyId,
        Guid? researchRunId,
        IReadOnlyCollection<SourceDocument> sources,
        IReadOnlyCollection<ResearchTarget>? requestedTargets = null,
        bool budgetExhausted = false)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var targets = (requestedTargets is null || requestedTargets.Count == 0
                ? DefaultTargets
                : requestedTargets.Distinct().ToArray())
            .OrderBy(target => target)
            .ToArray();

        var documents = sources
            .Where(source => companyId == Guid.Empty || source.CompanyId == companyId)
            .Where(source => researchRunId is null || source.ResearchRunId == researchRunId.Value)
            .Where(source => source.SourceKind != SourceKind.SearchResult)
            .Select(CreateSourceEvidence)
            .ToArray();

        var items = targets
            .Select(target => EvaluateTarget(target, documents, budgetExhausted))
            .ToArray();

        return new EvidenceCoverageResponse(companyId, researchRunId, items, budgetExhausted);
    }

    private EvidenceCoverageItem EvaluateTarget(
        ResearchTarget target,
        IReadOnlyList<SourceEvidence> documents,
        bool budgetExhausted)
    {
        var detected = documents
            .Where(document => ContainsTargetEvidence(target, document, includeDifferentEntity: true))
            .ToArray();

        var differentEntity = detected
            .Where(document => document.EntityRelationship == EntityRelationship.DifferentEntity)
            .ToArray();
        var direct = detected
            .Where(document => document.EntityRelationship == EntityRelationship.SameEntity)
            .ToArray();
        var related = detected
            .Where(document => document.EntityRelationship is
                EntityRelationship.Parent or EntityRelationship.Subsidiary or EntityRelationship.Affiliate or EntityRelationship.Uncertain)
            .ToArray();

        var sourcesByTarget = direct
            .OrderByDescending(document => AuthorityRank(target, document.Document.SourceKind))
            .ThenByDescending(document => document.HasStructuredFact)
            .ThenByDescending(document => document.Document.RetrievedAt)
            .ToArray();

        var strongest = sourcesByTarget.FirstOrDefault();
        var strongestRank = strongest is null
            ? 0
            : AuthorityRank(target, strongest.Document.SourceKind);
        var distinctDomains = direct
            .Select(GetIndependentSourceKey)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var level = DetermineLevel(target, sourcesByTarget, strongestRank, distinctDomains);
        var reasons = BuildReasons(
            target,
            level,
            sourcesByTarget,
            related,
            differentEntity,
            strongest,
            distinctDomains,
            budgetExhausted);

        return new EvidenceCoverageItem(
            target,
            level,
            sourcesByTarget.Length,
            strongest?.Document.SourceKind,
            reasons);
    }

    private CoverageLevel DetermineLevel(
        ResearchTarget target,
        IReadOnlyList<SourceEvidence> direct,
        int strongestRank,
        int distinctDomains)
    {
        if (direct.Count == 0)
        {
            return CoverageLevel.Missing;
        }

        var strongest = direct[0];
        if (IsStrongEvidence(target, strongest, strongestRank))
        {
            return CoverageLevel.Strong;
        }

        // Independent corroboration can make a weakly structured field usable,
        // but it does not turn a low-authority source into an authoritative one.
        if (strongestRank >= SupportedRank(target) && distinctDomains >= 2)
        {
            return CoverageLevel.Supported;
        }

        return strongestRank >= SupportedRank(target)
            ? CoverageLevel.Supported
            : CoverageLevel.Weak;
    }

    private bool IsStrongEvidence(ResearchTarget target, SourceEvidence evidence, int rank)
    {
        var kind = evidence.Document.SourceKind;
        if (rank == 0 || !evidence.HasDirectFact)
        {
            return false;
        }

        return target switch
        {
            ResearchTarget.LegalIdentity or ResearchTarget.TaxRegistration =>
                kind is SourceKind.OfficialBusinessRegistry or SourceKind.OfficialDocument,
            ResearchTarget.FoundedHistory =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            ResearchTarget.Industry =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument or SourceKind.OfficialBusinessRegistry,
            ResearchTarget.EmployeeScale =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            ResearchTarget.ProductsServices =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            ResearchTarget.Markets =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            ResearchTarget.Leadership =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            ResearchTarget.Locations =>
                kind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            _ => false
        } && rank >= StrongRank(target);
    }

    private int AuthorityRank(ResearchTarget target, SourceKind sourceKind)
    {
        var fields = target switch
        {
            ResearchTarget.LegalIdentity => new[] { SourceField.LegalIdentity },
            ResearchTarget.TaxRegistration => new[] { SourceField.TaxRegistration },
            ResearchTarget.Industry => new[] { SourceField.RegisteredBusinessActivities },
            ResearchTarget.EmployeeScale => new[] { SourceField.EmployeeScale },
            ResearchTarget.ProductsServices => new[] { SourceField.ProductsServices },
            ResearchTarget.Leadership => new[] { SourceField.Leadership },
            ResearchTarget.Locations => new[] { SourceField.OperatingLocations, SourceField.RegisteredAddress },
            _ => Array.Empty<SourceField>()
        };

        if (fields.Length > 0)
        {
            return fields.Max(field => authorityPolicy.GetRank(field, sourceKind));
        }

        return sourceKind switch
        {
            SourceKind.OfficialWebsite => 100,
            SourceKind.OfficialDocument => 95,
            SourceKind.News => 70,
            SourceKind.TopCv => 65,
            SourceKind.LinkedIn => 55,
            SourceKind.OfficialBusinessRegistry => 45,
            SourceKind.BusinessDirectory => 40,
            SourceKind.BusinessRegistry => 35,
            SourceKind.ExternalWebsite => 45,
            _ => 0
        };
    }

    private static int StrongRank(ResearchTarget target) => target switch
    {
        ResearchTarget.LegalIdentity or ResearchTarget.TaxRegistration => 100,
        ResearchTarget.FoundedHistory => 80,
        ResearchTarget.Industry => 80,
        ResearchTarget.EmployeeScale => 85,
        ResearchTarget.ProductsServices => 90,
        ResearchTarget.Markets => 80,
        ResearchTarget.Leadership => 90,
        ResearchTarget.Locations => 90,
        _ => 100
    };

    private static int SupportedRank(ResearchTarget target) => target switch
    {
        ResearchTarget.LegalIdentity or ResearchTarget.TaxRegistration => 75,
        ResearchTarget.FoundedHistory => 45,
        ResearchTarget.Industry => 50,
        ResearchTarget.EmployeeScale => 65,
        ResearchTarget.ProductsServices => 65,
        // A vague external article should not establish a market, and a
        // LinkedIn snippet alone is not leadership evidence. News or a
        // corroborating directory can still reach Supported through these
        // higher field-specific thresholds.
        ResearchTarget.Markets => 55,
        ResearchTarget.Leadership => 75,
        ResearchTarget.Locations => 60,
        _ => 0
    };

    private static bool ContainsTargetEvidence(
        ResearchTarget target,
        SourceEvidence source,
        bool includeDifferentEntity = false)
    {
        if (source.Document.SourceKind == SourceKind.SearchResult ||
            (!includeDifferentEntity && source.EntityRelationship == EntityRelationship.DifferentEntity))
        {
            return false;
        }

        var structured = source.Facts.Keys.ToHashSet(StringComparer.Ordinal);
        var text = source.SearchText;

        // Registered business activities are legal classifications. They are
        // deliberately not accepted as marketed products or services.
        if (target == ResearchTarget.ProductsServices &&
            (source.Document.SourceKind is SourceKind.BusinessDirectory or SourceKind.OfficialBusinessRegistry or SourceKind.BusinessRegistry) &&
            !HasAnyFact(structured, "productsservices", "products", "services", "solutions"))
        {
            return false;
        }

        var factKeys = target switch
        {
            ResearchTarget.LegalIdentity => new[] { "legalname", "internationalname", "companyname", "businessname" },
            ResearchTarget.TaxRegistration => new[] { "taxid", "taxcode", "taxidentificationnumber", "registrationnumber", "registrationnumberortaxid" },
            ResearchTarget.FoundedHistory => new[] { "foundedyear", "founded", "established", "history" },
            ResearchTarget.Industry => new[] { "industry", "primaryindustry", "secondaryindustries", "registeredbusinessactivities", "businessactivities" },
            ResearchTarget.EmployeeScale => new[] { "employeecount", "employeecountrange", "employees", "companysize", "workforce" },
            ResearchTarget.ProductsServices => new[] { "productsservices", "products", "services", "solutions", "platforms" },
            ResearchTarget.Markets => new[] { "markets", "customers", "clients", "countries", "internationalmarkets" },
            ResearchTarget.Leadership => new[] { "leadership", "management", "board", "executives", "ceo", "representative" },
            ResearchTarget.Locations => new[] { "locations", "offices", "address", "registeredaddress", "headquarters", "operatinglocations" },
            _ => Array.Empty<string>()
        };

        if (HasAnyFact(structured, factKeys))
        {
            return true;
        }

        return target switch
        {
            ResearchTarget.LegalIdentity => HasAny(text, "legal name", "company name", "business name", "registered company", "ten doanh nghiep", "tên doanh nghiệp"),
            ResearchTarget.TaxRegistration => HasTaxSignal(text),
            ResearchTarget.FoundedHistory => HasAny(text, "founded", "established", "company history", "our history", "thanh lap", "thành lập"),
            ResearchTarget.Industry => HasAny(text, "industry", "business activities", "business field", "sector", "what we do", "nganh nghe", "ngành nghề", "linh vuc", "lĩnh vực"),
            ResearchTarget.EmployeeScale => HasAny(text, "employees", "employee", "workforce", "company size", "staff", "quy mo cong ty", "quy mô công ty", "nhan vien", "nhân viên"),
            ResearchTarget.ProductsServices => HasAny(text, "products", "product", "services", "service", "solutions", "platform", "san pham", "sản phẩm", "dich vu", "dịch vụ", "giai phap", "giải pháp"),
            ResearchTarget.Markets => HasAny(text, "markets", "customers", "clients", "global", "international", "countries", "thi truong", "thị trường", "khach hang", "khách hàng"),
            ResearchTarget.Leadership => HasAny(text, "leadership", "management", "board", "executive", "ceo", "chief executive", "general director", "director", "representative", "lanh dao", "lãnh đạo", "nguoi dai dien", "người đại diện"),
            ResearchTarget.Locations => HasAny(text, "locations", "offices", "office", "headquarters", "contact", "address", "dia chi", "địa chỉ", "tru so", "trụ sở", "van phong", "văn phòng"),
            _ => false
        };
    }

    private static bool HasTaxSignal(string text) =>
        HasAny(text, "tax id", "tax code", "tax identification", "registration number", "ma so thue", "mã số thuế", "ma so doanh nghiep", "mã số doanh nghiệp") ||
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!\d)\d{10}(?:-\d{3})?(?!\d)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool HasAnyFact(ISet<string> facts, params string[] keys) => keys.Any(facts.Contains);

    private static bool HasAny(string text, params string[] signals) => signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static SourceEvidence CreateSourceEvidence(SourceDocument document)
    {
        var facts = ParseFacts(document.StructuredFactsJson);
        var relation = ParseEntityRelationship(facts);
        var text = string.Join(' ', document.Title, document.Url, document.SourceDomain, document.Content);
        return new SourceEvidence(document, facts, relation, text);
    }

    private static Dictionary<string, string[]> ParseFacts(string? json)
    {
        var facts = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return facts;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return facts;
            }

            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                var values = ReadValues(property.Value);
                if (values.Length > 0)
                {
                    facts[NormalizeKey(property.Name)] = values;
                }
            }
        }
        catch (JsonException)
        {
            // Structured facts are an optimization. The raw document remains
            // usable evidence when a provider emits malformed JSON.
        }

        return facts;
    }

    private static string[] ReadValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .SelectMany(ReadValues)
                .Where(item => item.Length > 0)
                .ToArray();
        }

        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            var text = value.ToString().Trim();
            return text.Length == 0 ? Array.Empty<string>() : [text];
        }

        return Array.Empty<string>();
    }

    private static EntityRelationship ParseEntityRelationship(IReadOnlyDictionary<string, string[]> facts)
    {
        if (facts.TryGetValue("sameentity", out var sameEntity) && sameEntity.Any(value => bool.TryParse(value, out var same) && !same))
        {
            return EntityRelationship.DifferentEntity;
        }

        if (!facts.TryGetValue("entityrelationship", out var values) && !facts.TryGetValue("relationship", out values))
        {
            return EntityRelationship.SameEntity;
        }

        var value = values.FirstOrDefault() ?? string.Empty;
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.ToLowerInvariant() switch
        {
            "sameentity" or "same" => EntityRelationship.SameEntity,
            "parent" or "parentgroup" => EntityRelationship.Parent,
            "subsidiary" => EntityRelationship.Subsidiary,
            "affiliate" => EntityRelationship.Affiliate,
            "differententity" or "different" or "unrelated" => EntityRelationship.DifferentEntity,
            "uncertain" or "unknown" => EntityRelationship.Uncertain,
            _ => EntityRelationship.Uncertain
        };
    }

    private static string NormalizeKey(string key) => new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string GetIndependentSourceKey(SourceEvidence source)
    {
        if (Uri.TryCreate(source.Document.NormalizedUrl ?? source.Document.Url, UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }

        return source.Document.SourceDomain?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static IReadOnlyList<string> BuildReasons(
        ResearchTarget target,
        CoverageLevel level,
        IReadOnlyList<SourceEvidence> direct,
        IReadOnlyList<SourceEvidence> related,
        IReadOnlyList<SourceEvidence> differentEntity,
        SourceEvidence? strongest,
        int distinctDomains,
        bool budgetExhausted)
    {
        var reasons = new List<string>();
        if (direct.Count == 0)
        {
            reasons.Add($"No same-entity acquired evidence was found for {target}.");
            if (related.Count > 0)
            {
                reasons.Add("Acquired evidence is related-company context and was not counted as direct dossier support.");
            }

            if (differentEntity.Count > 0)
            {
                reasons.Add("Different-entity sources were excluded from coverage.");
            }

            if (budgetExhausted)
            {
                reasons.Add("Research budget exhausted; the field remains unknown.");
            }

            return reasons;
        }

        reasons.Add($"{direct.Count} same-entity source{(direct.Count == 1 ? "" : "s")} support{(direct.Count == 1 ? "s" : "")} {target}.");
        if (strongest is not null)
        {
            reasons.Add($"Strongest source kind: {strongest.Document.SourceKind}.");
            if (strongest.HasStructuredFact)
            {
                reasons.Add("Structured source facts were available for this target.");
            }

            if (IsMaSoThue(strongest.Document))
            {
                reasons.Add("MaSoThue facts are third-party directory corroboration, not an official government registry.");
            }
        }

        if (distinctDomains > 1)
        {
            reasons.Add($"Evidence is corroborated across {distinctDomains} independent source domains.");
        }

        if (related.Count > 0)
        {
            reasons.Add("Related-company context was kept separate from same-entity coverage.");
        }

        if (level is CoverageLevel.Weak or CoverageLevel.Missing)
        {
            reasons.Add("Source authority or field-specific detail is limited.");
        }

        if (budgetExhausted && level is CoverageLevel.Missing or CoverageLevel.Weak)
        {
            reasons.Add("Research budget exhausted; unsupported details remain unknown.");
        }

        return reasons;
    }

    private static bool IsMaSoThue(SourceDocument document) =>
        Uri.TryCreate(document.Url, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("masothue.com", StringComparison.OrdinalIgnoreCase);

    private sealed record SourceEvidence(
        SourceDocument Document,
        IReadOnlyDictionary<string, string[]> Facts,
        EntityRelationship EntityRelationship,
        string SearchText)
    {
        public bool HasStructuredFact => Facts.Count > 0;
        public bool HasDirectFact => HasStructuredFact || SearchText.Length > 0;
    }
}
