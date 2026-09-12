namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Groups records using strong, explainable identity keys. It deliberately
/// does not call an AI provider and does not merge or mutate records.
/// </summary>
public sealed class CompanyDuplicateGroupingService : ICompanyDuplicateGroupingService
{
    private static readonly DuplicateMatchType[] MatchPriority =
    [
        DuplicateMatchType.RegistrationNumber,
        DuplicateMatchType.WebsiteHost,
        DuplicateMatchType.LegalNameAndCountry,
        DuplicateMatchType.NameAndCountry
    ];

    /// <summary>Convenience adapter for read-only Company collections.</summary>
    public IReadOnlyList<CompanyDuplicateGroup> Group(IEnumerable<Company> companies)
    {
        ArgumentNullException.ThrowIfNull(companies);
        return Group(companies.Select(company => CompanyWorkspaceSnapshot.FromCompany(company)).ToArray());
    }

    public IReadOnlyList<CompanyDuplicateGroup> Group(
        IReadOnlyCollection<CompanyWorkspaceSnapshot> companies)
    {
        ArgumentNullException.ThrowIfNull(companies);
        if (companies.Count < 2)
        {
            return [];
        }

        var records = companies
            .Where(snapshot => snapshot is not null)
            .GroupBy(snapshot => snapshot.Company.Id)
            .Select(group => group
                .OrderBy(snapshot => snapshot.Company.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(snapshot => snapshot.Company.Id)
            .ToArray();

        if (records.Length < 2)
        {
            return [];
        }

        var parent = Enumerable.Range(0, records.Length).ToArray();
        var rank = new int[records.Length];
        var keys = BuildKeys(records);

        foreach (var members in keys.Values)
        {
            var first = members[0].Index;
            foreach (var member in members.Skip(1))
            {
                Union(parent, rank, first, member.Index);
            }
        }

        var components = Enumerable.Range(0, records.Length)
            .GroupBy(index => Find(parent, index))
            .Where(group => group.Count() > 1)
            .Select(group => BuildGroup(records, group, keys))
            .OrderBy(group => group.Members[0].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Members[0].CompanyId)
            .ToArray();

        return components;
    }

    private static Dictionary<string, List<(int Index, DuplicateMatchType Type)>> BuildKeys(
        IReadOnlyList<CompanyWorkspaceSnapshot> records)
    {
        var keys = new Dictionary<string, List<(int Index, DuplicateMatchType Type)>>(StringComparer.Ordinal);
        for (var index = 0; index < records.Count; index++)
        {
            foreach (var (key, type) in IdentityKeys(records[index]))
            {
                if (!keys.TryGetValue(key, out var members))
                {
                    members = [];
                    keys[key] = members;
                }

                members.Add((index, type));
            }
        }

        return keys
            .Where(pair => pair.Value.Count > 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static IEnumerable<(string Key, DuplicateMatchType Type)> IdentityKeys(
        CompanyWorkspaceSnapshot snapshot)
    {
        // Workspace fields are the first source of identity signals. Accepted
        // profile values are an evidence-backed fallback for legacy/sparse
        // Company rows; model-only identity hints never enter this path.
        foreach (var key in IdentityKeys(snapshot.Company))
        {
            yield return key;
        }

        if (snapshot.AcceptedProfile is not { } profile)
        {
            yield break;
        }

        var profileRegistration = CompanyIdentityNormalizer.NormalizeRegistration(profile.RegistrationNumberOrTaxId);
        if (profileRegistration is not null)
        {
            yield return ($"registration:{profileRegistration}", DuplicateMatchType.RegistrationNumber);
        }

        var profileWebsiteHost = CompanyIdentityNormalizer.NormalizeWebsiteHost(profile.Website);
        if (profileWebsiteHost is not null)
        {
            yield return ($"website:{profileWebsiteHost}", DuplicateMatchType.WebsiteHost);
        }

        var profileCountry = CompanyIdentityNormalizer.NormalizeName(profile.Country ?? snapshot.Company.Country);
        var profileLegalName = CompanyIdentityNormalizer.NormalizeName(profile.LegalName);
        if (profileCountry is not null && profileLegalName is not null)
        {
            yield return ($"legal:{profileLegalName}|country:{profileCountry}", DuplicateMatchType.LegalNameAndCountry);
        }

        var profileDisplayName = CompanyIdentityNormalizer.NormalizeName(profile.DisplayName);
        if (profileCountry is not null && profileDisplayName is not null)
        {
            yield return ($"name:{profileDisplayName}|country:{profileCountry}", DuplicateMatchType.NameAndCountry);
        }
    }

    private static IEnumerable<(string Key, DuplicateMatchType Type)> IdentityKeys(Company company)
    {
        var registration = CompanyIdentityNormalizer.NormalizeRegistration(company.RegistrationNumber);
        if (registration is not null)
        {
            yield return ($"registration:{registration}", DuplicateMatchType.RegistrationNumber);
        }

        var websiteHost = CompanyIdentityNormalizer.NormalizeWebsiteHost(company.Website);
        if (websiteHost is not null)
        {
            yield return ($"website:{websiteHost}", DuplicateMatchType.WebsiteHost);
        }

        var country = CompanyIdentityNormalizer.NormalizeName(company.Country);
        var legalName = CompanyIdentityNormalizer.NormalizeName(company.LegalName);
        if (country is not null && legalName is not null)
        {
            yield return ($"legal:{legalName}|country:{country}", DuplicateMatchType.LegalNameAndCountry);
        }

        var name = CompanyIdentityNormalizer.NormalizeName(company.Name);
        if (country is not null && name is not null)
        {
            yield return ($"name:{name}|country:{country}", DuplicateMatchType.NameAndCountry);
        }
    }

    private static CompanyDuplicateGroup BuildGroup(
        IReadOnlyList<CompanyWorkspaceSnapshot> allRecords,
        IEnumerable<int> componentIndices,
        IReadOnlyDictionary<string, List<(int Index, DuplicateMatchType Type)>> keys)
    {
        var componentIndexArray = componentIndices.ToArray();
        var componentIndexSet = componentIndexArray.ToHashSet();
        var recordList = componentIndexArray.Select(index => allRecords[index]).ToArray();
        var matchTypes = keys
            .Where(pair => pair.Value.Any(member => componentIndexSet.Contains(member.Index)))
            .SelectMany(pair => pair.Value.Select(member => member.Type))
            .Distinct()
            .OrderBy(type => Array.IndexOf(MatchPriority, type))
            .ToArray();

        var strongest = matchTypes.FirstOrDefault();
        var members = recordList
            .Select(record => new CompanyWorkspaceIdentity(
                record.Company.Id,
                record.Company.Name,
                record.Company.LegalName,
                record.Company.Website,
                record.Company.Country,
                record.Company.RegistrationNumber))
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.CompanyId)
            .ToArray();

        var groupId = $"duplicate:{members[0].CompanyId:N}";
        return new CompanyDuplicateGroup(
            groupId,
            strongest,
            matchTypes,
            members,
            BuildRationale(matchTypes));
    }

    private static string BuildRationale(IReadOnlyList<DuplicateMatchType> matchTypes) => matchTypes.Count switch
    {
        0 => "Records share no deterministic identity signal.",
        1 => matchTypes[0] switch
        {
            DuplicateMatchType.RegistrationNumber => "Exact normalized registration or tax ID match.",
            DuplicateMatchType.WebsiteHost => "Exact normalized website host match.",
            DuplicateMatchType.LegalNameAndCountry => "Normalized legal name and country match.",
            DuplicateMatchType.NameAndCountry => "Normalized company name and country match.",
            _ => "Deterministic identity signals match."
        },
        _ => $"Multiple deterministic identity signals match: {string.Join(", ", matchTypes)}."
    };

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int[] rank, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot == rightRoot)
        {
            return;
        }

        if (rank[leftRoot] < rank[rightRoot])
        {
            parent[leftRoot] = rightRoot;
        }
        else if (rank[leftRoot] > rank[rightRoot])
        {
            parent[rightRoot] = leftRoot;
        }
        else
        {
            parent[rightRoot] = leftRoot;
            rank[leftRoot]++;
        }
    }
}
