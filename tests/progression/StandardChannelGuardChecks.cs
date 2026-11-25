using SabberStoneCore.Model;

// STO-2 Wild mode: the Wild channel runs next to the Standard one, so the Standard behaviour from before that work
// must be unchanged. These are the three places where a Wild change could have widened Standard by accident: the
// policy split behind the pools, the deck-admission gate, and the start validation of a personal Standard deck.
internal static class StandardChannelGuardChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        throw new Exception("Expected rejection: " + message);
    }

    // AT_004 is a manifest Past card outside the Standard ledger: no collection, pack or craft path grants it, and
    // only the Wild pool draws it (tests/progression/WildOwnershipChecks.cs:12-14).
    private const string PastCard = "AT_004";

    // The catalog split is pinned on purpose (tests/engine-proof/WildPoolSupersetProof.cs:14-17): a card data sync
    // that moves a card between the pools has to be reviewed, not silently accepted by this guard.
    private const int StandardCount = 1139;
    private const int PastCount = 108;
    private const int WildCount = 1247;
    private const int InactiveCount = 130;

    // A. The Wild work left the pool split exactly as it was: Standard 1139, Past 108, Inactive 130, and the three
    // lists still partition the catalog with Wild being Standard plus Past.
    internal static void StandardChannel_PoolSplitUnchanged()
    {
        var policy = OfflinePoolPolicy.Load();
        var manifest = policy.BuildManifest();
        var standardIds = manifest.Standard.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var pastIds = manifest.Past.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        // Inactive is structural: the catalog members in neither list (tests/engine-proof/WildPoolSupersetProof.cs:53-60).
        var catalogIds = policy.Members.Select(m => m.CardId).ToHashSet(StringComparer.Ordinal);
        var inactiveIds = catalogIds.Where(id => !standardIds.Contains(id) && !pastIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        // The declared scope must agree with that structural definition, which also proves the three lists leave
        // no catalog member unclassified (tests/engine-proof/WildPoolSupersetProof.cs:53-64).
        var declaredInactive = policy.Members.Where(m => m.Scope == "Inactive").Select(m => m.CardId)
            .ToHashSet(StringComparer.Ordinal);
        var wildIds = standardIds.Union(pastIds).ToHashSet(StringComparer.Ordinal);

        Check(standardIds.Count == StandardCount, $"the Standard pool is {StandardCount} cards, got {standardIds.Count}");
        Check(pastIds.Count == PastCount, $"the Past pool is {PastCount} cards, got {pastIds.Count}");
        Check(!standardIds.Overlaps(pastIds), "Standard and Past share no card");
        Check(wildIds.Count == WildCount, $"Wild is {WildCount} cards, got {wildIds.Count}");
        Check(inactiveIds.Count == InactiveCount, $"Inactive is {InactiveCount} cards, got {inactiveIds.Count}");
        Check(!inactiveIds.Overlaps(standardIds), "Standard holds no Inactive card");
        Check(!inactiveIds.Overlaps(pastIds), "Past holds no Inactive card");
        Check(inactiveIds.SetEquals(declaredInactive), "Inactive is exactly the catalog members outside Standard and Past");
        Check(inactiveIds.Count > 0, "premise: the catalog holds Inactive members");
        Check(standardIds.Count + pastIds.Count + inactiveIds.Count == catalogIds.Count,
            $"Standard, Past and Inactive partition the catalog, got {catalogIds.Count} members");
    }

    // B. Standard admission is unchanged in both directions: a manifest Past card is still refused while a deck
    // built from the Standard pool is still admitted by the same gate with standardOnly at its default.
    internal static void StandardChannel_AdmissionUnchanged()
    {
        // standardOnly defaults to true (plugin/LocalShop.cs:584). The refusal comes from the ledger gate, since
        // AT_004 stays outside the Standard ledger (WildOwnershipChecks.cs:29-31).
        Reject(() => LocalShop.ValidateDeck("Standard guard", "HERO_08", new[] { PastCard }, null, false),
            "a Standard deck must still reject a manifest Past card");

        // The pass face proves standardOnly costs Standard nothing. The default Mage recipe cannot show that: it is a
        // legacy EXPERT1/CORE deck whose sets sit outside the Standard set list, so the same gate rejects it (measured:
        // all 15 distinct cards fail IsStandardCard, and the call above throws for it). A deck drawn from the manifest's
        // Standard list does show it: every Standard member is in the ledger and Standard, and 343 are Mage-legal.
        var deck = StandardMageDeck();
        LocalShop.ValidateDeck("Standard guard", "HERO_08", deck, null, false);
    }

    // C. A saved, fully owned Standard deck still walks the whole start check: IsDeckComplete -> ownership ->
    // MissingDeckSlots, then ValidateStart accepts it. The refusal counterpart is already covered by
    // WildStartE2E_StandardDeckStillRefused (tests/progression/WildStartE2EChecks.cs:75), so it is not repeated here.
    internal static void StandardChannel_StartValidationUnchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-standardguard-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "standard.xml");
            using (var profile = new LocalProfile(path))
            {
                // The default Mage recipe as it ships: no card is swapped for a Past or an unowned one.
                profile.SaveDeck("Standard guard", "HERO_08", LocalDecks.Get("mage").CardIds);
                var saved = profile.CustomDeck;
                Check(saved != null, "premise: the Standard deck saved");
                var complete = profile.IsDeckComplete(saved.Id);
                var resolved = complete ? profile.ResolveCustomDeck() : null;
                Check(complete, "a fully owned Standard deck must be complete");
                Check(resolved != null, "a complete Standard deck must resolve to a playable deck");
                var entries = NativeUIProjection.ValidateStart(profile, "custom:" + saved.Id, resolved, true);
                Check(entries != null && entries.Count == 30, "a Standard deck must pass the start check with its 30 copies");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // A deterministic 30-card Mage deck drawn from the installed Standard pool: the fixture the pass face needs,
    // because the shipped Mage recipe is a legacy deck that the Standard set gate rejects.
    private static List<string> StandardMageDeck()
    {
        var deck = OfflinePoolPolicy.Load().BuildManifest().Standard
            .Where(m => LocalProfile.KnownLedgerCard(m.CardId) && LocalShop.IsStandardCard(SabberStoneCore.Model.Cards.FromId(m.CardId)))
            .Where(m => LocalDecks.IsAllowedInDeck(SabberStoneCore.Model.Cards.FromId(m.CardId), LocalDecks.HeroClass("HERO_08")))
            .Where(m => !SabberStoneCore.Actions.FabledRules.IsFabled(m.CardId)
                && !SabberStoneCore.Actions.DeckbuildingRules.HasSideboard(m.CardId))
            .OrderBy(m => m.DbfId).Select(m => m.CardId).Distinct(StringComparer.Ordinal).Take(30).ToList();
        Check(deck.Count == 30, $"premise: the Standard pool holds a 30-card Mage deck, got {deck.Count}");
        return deck;
    }

    internal static void Run()
    {
        StandardChannel_PoolSplitUnchanged();
        StandardChannel_AdmissionUnchanged();
        StandardChannel_StartValidationUnchanged();
    }
}
