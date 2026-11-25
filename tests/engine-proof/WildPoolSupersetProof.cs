using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.RandomPools;

// STO-2 Wild mode: the Wild pool is the manifest's Standard list plus its Past list, and nothing else.
// The Inactive members (the next meta's cards) belong to no pool, and the frozen legacy AllWild list is never
// read: it still holds stub cards from unimplemented sets. The manifest is the authority for membership, so the
// 1247-card total is asserted there; the match pool is the same membership minus the cards no random effect may
// ever hand out (heroes, locations, Colossal bodies, Old Gods, three-rune cards, unimplemented cards), and that
// filter is applied per member, so the superset relation survives it exactly.
internal static class WildPoolSupersetProof
{
    // Standard 1139 + Past 108. The catalog also holds 130 Inactive members, which Wild must never reach.
    // Update this when a card data sync changes the catalog: the count is pinned on purpose, so a silent pool
    // change fails here instead of shipping.
    private const int WildMemberCount = 1247;

    private static void Check(bool value, string message) => Core2026Proof.Check(value, "wild pool: " + message);

    private static Game NewGame(GenerationFormat format)
    {
        var game = new Game(new GameConfig {
            RandomSeed = 5, StartPlayer = 1, Shuffle = false, SkipMulligan = true, Logging = false, History = true,
            FormatType = FormatType.FT_WILD, PoolFormat = format,
            Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = Enumerable.Repeat(Cards.FromId("CS2_179"), 30).ToList(),
            Player2Deck = Enumerable.Repeat(Cards.FromId("CS2_179"), 30).ToList() });
        game.StartGame();
        return game;
    }

    [GroupProof("wild-pool-superset")]
    internal static void Run()
    {
        var policy = OfflinePoolPolicy.Load();
        var manifest = policy.BuildManifest();
        var standardIds = manifest.Standard.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var pastIds = manifest.Past.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var inactiveIds = InactiveIds(policy, standardIds, pastIds);

        ManifestMembership(manifest, policy, standardIds, pastIds, inactiveIds);
        PolicyWildPool(policy, standardIds, pastIds, inactiveIds);
        WildExtraIsPast(manifest);
        PoolSuperset(standardIds, pastIds, inactiveIds);

        Console.WriteLine($"PASS wild pool: Wild is Standard ({manifest.Standard.Count}) plus Past ({manifest.Past.Count}) = " +
            $"{WildMemberCount} members, no Inactive member ({inactiveIds.Count} stay out), and the match pool keeps every Standard card");
    }

    // Inactive is defined structurally: the catalog members in neither list. The declared scope must agree, which
    // also proves Standard, Past and Inactive partition the catalog with no member left unclassified.
    private static HashSet<string> InactiveIds(OfflinePoolPolicy policy, HashSet<string> standardIds, HashSet<string> pastIds)
    {
        var catalogIds = policy.Members.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var inactiveIds = catalogIds.Where(id => !standardIds.Contains(id) && !pastIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        var declared = policy.Members.Where(m => m.Scope == "Inactive").Select(m => m.CardId)
            .ToHashSet(StringComparer.Ordinal);
        Check(inactiveIds.SetEquals(declared), "Inactive is exactly the catalog members outside Standard and Past");
        Check(inactiveIds.Count > 0, "premise: the catalog holds Inactive members");
        Check(standardIds.Count + pastIds.Count + inactiveIds.Count == catalogIds.Count,
            "documentation: Standard, Past and Inactive partition the catalog");
        return inactiveIds;
    }

    private static void ManifestMembership(OfflinePoolManifest manifest, OfflinePoolPolicy policy,
        HashSet<string> standardIds, HashSet<string> pastIds, HashSet<string> inactiveIds)
    {
        var standard = manifest.Standard;
        var past = manifest.Past;

        // (4) Neither list repeats a dbfId, and the two lists never share one. The manifest constructor already
        // refuses a duplicate catalog identity; this pins the split that Wild is built from.
        // OfflinePoolManifest.cs:52-55 already refuses a duplicate catalog identity across all members, so these
        // restate the constructor's guarantee; they are here to document the split Wild is built from.
        Check(standard.Select(m => m.DbfId).Distinct().Count() == standard.Count, "documentation: manifest Standard repeats no dbfId");
        Check(past.Select(m => m.DbfId).Distinct().Count() == past.Count, "documentation: manifest Past repeats no dbfId");
        Check(standard.Select(m => m.CardId).Distinct(StringComparer.Ordinal).Count() == standard.Count,
            "documentation: manifest Standard repeats no cardId");
        Check(past.Select(m => m.CardId).Distinct(StringComparer.Ordinal).Count() == past.Count,
            "documentation: manifest Past repeats no cardId");
        Check(!standard.Select(m => m.DbfId).ToHashSet().Overlaps(past.Select(m => m.DbfId)),
            "documentation: Standard and Past share no dbfId");
        Check(!standardIds.Overlaps(pastIds), "documentation: Standard and Past share no cardId");

        // (2) Wild = Standard union Past, and that union is exactly 1247 cards.
        var wildIds = standardIds.Union(pastIds).ToHashSet(StringComparer.Ordinal);
        Check(wildIds.Count == standard.Count + past.Count, "documentation: the union adds no member twice");
        Check(wildIds.Count == WildMemberCount,
            $"Wild is {WildMemberCount} cards, got {wildIds.Count} (Standard {standard.Count} + Past {past.Count})");

        // (3) Wild holds no Inactive member.
        Check(!wildIds.Overlaps(inactiveIds), "documentation: Wild holds no Inactive member");

        // The manifest is the installed policy's own catalog, not a fixture.
        Check(manifest.PolicyVersion == policy.PolicyVersion, "the manifest carries the installed policy version");
        Check(standard.Count + past.Count + inactiveIds.Count == policy.Members.Count,
            "documentation: the manifest accounts for every catalog member");
    }

    // The policy-level Wild view: Standard union Past, derived from the member scopes rather than a catalog meta.
    private static void PolicyWildPool(OfflinePoolPolicy policy, HashSet<string> standardIds, HashSet<string> pastIds,
        HashSet<string> inactiveIds)
    {
        var wild = policy.WildPool;
        var wildIds = wild.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);

        Check(wild.Count == standardIds.Count + pastIds.Count,
            $"the policy Wild pool is {wild.Count} members, Standard {standardIds.Count} + Past {pastIds.Count}");
        // OfflinePoolPolicy.cs:52 builds WildPool as members.Where(scope != "Inactive").OrderBy(dbfId), so the
        // following follow from that line; :105 above is the one that measures the result.
        Check(standardIds.IsSubsetOf(wildIds), "documentation: the policy Wild pool keeps every Standard member");
        Check(pastIds.IsSubsetOf(wildIds), "documentation: the policy Wild pool holds every Past member");
        Check(!wildIds.Overlaps(inactiveIds), "documentation: the policy Wild pool holds no Inactive member");
        Check(wild.Select(m => m.DbfId).Distinct().Count() == wild.Count, "documentation: the policy Wild pool repeats no dbfId");
        Check(wild.Select(m => m.DbfId).SequenceEqual(wild.Select(m => m.DbfId).OrderBy(id => id)),
            "documentation: the policy Wild pool is ordered by dbfId");
        Check(wild.All(m => m.Scope != "Inactive"), "documentation: no member of the policy Wild pool declares the Inactive scope");
    }

    // The one line under test: WildExtra is the manifest's Past list, not an empty list and not AllWild.
    private static void WildExtraIsPast(OfflinePoolManifest manifest)
    {
        var extra = MatchRandomPool.WildExtra(manifest);
        Check(extra.Count == manifest.Past.Count, $"WildExtra has {extra.Count} members, Past has {manifest.Past.Count}");
        Check(extra.Select(m => (m.DbfId, m.CardId)).SequenceEqual(manifest.Past.Select(m => (m.DbfId, m.CardId))),
            "WildExtra is exactly the manifest Past list, in order");
        Check(extra.Count > 0, "premise: Past is not empty, so Wild really is larger than Standard");
    }

    private static void PoolSuperset(HashSet<string> standardIds, HashSet<string> pastIds, HashSet<string> inactiveIds)
    {
        var standardGame = NewGame(GenerationFormat.Standard);
        var wildGame = NewGame(GenerationFormat.Wild);
        var standard = Cards.RandomPool(standardGame, PoolDestination.Hand);
        var wild = Cards.RandomPool(wildGame, PoolDestination.Hand);
        Check(!standard.Legacy && !wild.Legacy, "both pools are new-mode pools");
        Check(standard.Format == GenerationFormat.Standard && wild.Format == GenerationFormat.Wild,
            "each pool follows its game's policy");

        var standardPoolIds = standard.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var wildIds = wild.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var wildManifestIds = standardIds.Union(pastIds).ToHashSet(StringComparer.Ordinal);

        // (1) Wild keeps every Standard card.
        Check(standardPoolIds.IsSubsetOf(wildIds), "the Wild pool keeps every Standard pool card");
        // (2) Wild adds only Past members.
        Check(wildIds.IsSubsetOf(wildManifestIds), "the Wild pool adds only manifest Past members");
        // (3) Wild never adds an Inactive card.
        Check(!wildIds.Overlaps(inactiveIds), "documentation: the Wild pool holds no Inactive card");
        Check(wildIds.Count > standardPoolIds.Count, "premise: Past adds cards to the Wild pool");
        // The pool is the filtered subset of the 1247-member manifest, so its size is smaller by design.
        Check(wildIds.Count < WildMemberCount, "the pool is the filtered subset of the 1247-member manifest");

        // The member metadata behind the pool: Wild is Standard's surviving members followed by the Past members
        // that survive the same filter, and nothing else.
        var standardMembers = standard.Members;
        var wildMembers = wild.Members;
        Check(wildMembers.Count == wild.All.Count, "the Wild pool keeps one card per member");
        Check(wildMembers.Select(m => m.DbfId).Distinct().Count() == wildMembers.Count, "the Wild pool repeats no member");
        Check(wildMembers.Take(standardMembers.Count).Select(m => m.DbfId).SequenceEqual(standardMembers.Select(m => m.DbfId)),
            "the Wild pool starts with the Standard members, in order");
        var added = wildMembers.Skip(standardMembers.Count).ToArray();
        Check(added.Length > 0, "premise: Past contributes members to the Wild pool");
        Check(added.All(m => pastIds.Contains(m.CardId)), "every member Wild adds is a manifest Past member");
        Check(added.All(m => !standardIds.Contains(m.CardId)), "documentation: no member Wild adds is already a Standard member");

        // Cross-check against the other Wild definition: OfflineCardPools.Candidates(PoolScope.Wild) is the
        // membership the WildExtra comment points at. The match pool applies extra exclusions, so it must be a
        // subset of those candidates, and those candidates must themselves stay inside Standard union Past.
        var candidates = OfflineCardPools.Candidates(wildGame, CardClass.MAGE,
            OfflinePoolFilter.Random(scope: PoolScope.Wild), 0, PoolDestination.Hand);
        var candidateIds = candidates.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        Check(wildIds.IsSubsetOf(candidateIds), "the Wild pool stays inside OfflineCardPools' Wild candidates");
        Check(candidateIds.IsSubsetOf(wildManifestIds), "the Wild candidates add only manifest Past members");
        Check(!candidateIds.Overlaps(inactiveIds), "documentation: the Wild candidates hold no Inactive card");
        Check(candidateIds.Count > standardPoolIds.Count, "premise: the Wild candidates are larger than the Standard pool");

        Console.WriteLine($"  members: Standard {standardMembers.Count}, Wild {wildMembers.Count} (+{added.Length}), " +
            $"candidates {candidateIds.Count}, manifest {wildManifestIds.Count}");
    }
}
