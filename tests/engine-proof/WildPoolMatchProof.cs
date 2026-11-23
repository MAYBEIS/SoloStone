using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.RandomPools;

// STO-2 Wild mode: the match's random-effect pool follows the decks. A deck holding a Wild-only card (a manifest
// Past member) draws from the Wild pool; every other deck keeps the Standard pool, so the 30 pinned goldens and
// every existing replay stay on match-random/1. The plugin wiring that consumes this lives in plugin/NativeMatch.cs
// and is pinned by tests/native-bindings-logic/PluginSourcePins.cs, because the CI engine-proof build has no client.
internal static class WildPoolMatchProof
{
    private static void Check(bool value, string message) => Core2026Proof.Check(value, "wild pool match: " + message);

    [GroupProof("wild-pool-match")]
    internal static void Run()
    {
        var policy = OfflinePoolPolicy.Load();
        var manifest = policy.BuildManifest();
        var standardIds = manifest.Standard.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var pastIds = manifest.Past.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var inactiveIds = policy.Members.Where(m => m.Scope == "Inactive").Select(m => m.CardId)
            .ToHashSet(StringComparer.Ordinal);

        WildOnlyIds(policy, standardIds, pastIds, inactiveIds);
        RequiresWildPool(policy, standardIds, pastIds, inactiveIds);
        PoolFollowsThePolicy(standardIds, pastIds);

        Console.WriteLine($"PASS wild pool match: {policy.WildOnlyCardIds.Count} Wild-only cards switch the pool, " +
            "a Standard deck keeps the Standard pool, and the pool policy version is unchanged");
    }

    // The Wild-only set is exactly the manifest Past list: no Standard card, no Inactive card, no duplicate.
    private static void WildOnlyIds(OfflinePoolPolicy policy, HashSet<string> standardIds, HashSet<string> pastIds,
        HashSet<string> inactiveIds)
    {
        var wildOnly = policy.WildOnlyCardIds;
        Check(wildOnly.Count == pastIds.Count, $"WildOnlyCardIds has {wildOnly.Count} cards, Past has {pastIds.Count}");
        Check(wildOnly.ToHashSet(StringComparer.Ordinal).SetEquals(pastIds), "WildOnlyCardIds is exactly the manifest Past list");
        Check(wildOnly.Distinct(StringComparer.Ordinal).Count() == wildOnly.Count, "WildOnlyCardIds repeats no card");
        Check(!wildOnly.Any(standardIds.Contains), "no Wild-only card is a Standard member");
        Check(!wildOnly.Any(inactiveIds.Contains), "no Wild-only card is an Inactive member");
        Check(wildOnly.Count > 0, "premise: the catalog holds Wild-only cards");
    }

    // The derivation the match uses: one Wild-only card in either deck is enough, and nothing else switches the pool.
    private static void RequiresWildPool(OfflinePoolPolicy policy, HashSet<string> standardIds, HashSet<string> pastIds,
        HashSet<string> inactiveIds)
    {
        var standardDeck = standardIds.Take(30).Select(Cards.FromId).ToArray();
        var wildOnlyCard = Cards.FromId(policy.WildOnlyCardIds.First());
        var inactiveCard = Cards.FromId(inactiveIds.First());

        Check(!policy.RequiresWildPool(standardDeck), "a Standard deck keeps the Standard pool");
        Check(!policy.RequiresWildPool(Array.Empty<Card>()), "an empty deck keeps the Standard pool");
        Check(!policy.RequiresWildPool(null), "a missing deck keeps the Standard pool");
        Check(policy.RequiresWildPool(new[] { wildOnlyCard }), "one Wild-only card switches the pool");
        Check(policy.RequiresWildPool(standardDeck.Append(wildOnlyCard)), "a Wild-only card among Standard cards switches the pool");
        // Inactive members belong to no pool, so they must not silently open the Wild pool.
        Check(!policy.RequiresWildPool(new[] { inactiveCard }), "an Inactive card does not switch the pool");
        Check(pastIds.Contains(wildOnlyCard.Id), "the card that switches the pool is a manifest Past member");
    }

    // The engine side of the wiring: the same decks that switch the derivation also switch the pool, and the
    // Standard pool is untouched, which is what keeps the pinned goldens on match-random/1.
    private static void PoolFollowsThePolicy(HashSet<string> standardIds, HashSet<string> pastIds)
    {
        var standardGame = NewGame(GenerationFormat.Standard);
        var wildGame = NewGame(GenerationFormat.Wild);
        var standardPool = Cards.RandomPool(standardGame, PoolDestination.Hand);
        var wildPool = Cards.RandomPool(wildGame, PoolDestination.Hand);
        var standardPoolIds = standardPool.All.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        var wildPoolIds = wildPool.All.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);

        Check(standardPoolIds.IsSubsetOf(wildPoolIds), "the Wild pool keeps every Standard pool card");
        Check(wildPoolIds.Count > standardPoolIds.Count, "premise: the Wild pool is larger than the Standard one");
        var added = wildPoolIds.Except(standardPoolIds).ToArray();
        Check(added.All(pastIds.Contains), "every card the Wild pool adds is a manifest Past member");
        Check(added.All(id => !standardIds.Contains(id)), "no card the Wild pool adds is a Standard member");

        // The Standard path is byte-identical, so the 30 pinned goldens keep their recorded policy version.
        Check(MatchRandomPool.PolicyVersion == "match-random/1",
            $"the pool policy version stays match-random/1, got {MatchRandomPool.PolicyVersion}");

        Console.WriteLine($"  pools: Standard {standardPoolIds.Count}, Wild {wildPoolIds.Count} (+{added.Length})");
    }

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
}
