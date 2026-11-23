internal static class WildStartChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    // AT_004 is a manifest Past card outside the Standard ledger; Wild admits it, Standard does not.
    private const string PastCard = "AT_004";

    // A saved Wild deck must be startable: ownership is satisfied by the Wild admission channel, not the ledger.
    internal static void WildStart_PastCardDeckIsComplete()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "wild.xml");
            using (var profile = new LocalProfile(path))
            {
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = PastCard;
                profile.SaveDeck("Wild start", "HERO_08", cards);
                Check(profile.IsDeckComplete(profile.CustomDeck.Id), "Wild deck with a Past card must be complete");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // Standard keeps today's rule: an unowned Standard card still makes the deck incomplete.
    internal static void WildStart_StandardDeckStillNeedsOwnership()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "standard.xml");
            using (var profile = new LocalProfile(path))
            {
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = "CS2_029";
                profile.SaveDeck("Standard start", "HERO_08", cards);
                Check(!profile.IsDeckComplete(profile.CustomDeck.Id), "Standard deck with an unowned card must stay incomplete");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // The start-time ownership map raises only Wild-admitted cards, never Standard ones.
    internal static void WildStart_StartOwnedRaisesOnlyWildCards()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "owned.xml");
            using (var profile = new LocalProfile(path))
            {
                var owned = NativeUIProjection.StartOwned(profile, new[] { PastCard, "CS2_029" });
                Check(owned.TryGetValue(PastCard, out int wild) && wild >= LocalEconomy.MaximumPlayset,
                    "StartOwned must raise a Wild-admitted card to a playset");
                Check(!owned.TryGetValue("CS2_029", out int standard) || standard == 0,
                    "StartOwned must not raise a Standard card");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        WildStart_PastCardDeckIsComplete();
        WildStart_StandardDeckStillNeedsOwnership();
        WildStart_StartOwnedRaisesOnlyWildCards();
    }
}
