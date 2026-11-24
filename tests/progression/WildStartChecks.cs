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
                cards[0] = "EX1_287";
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
                var owned = NativeUIProjection.StartOwned(profile, new[] { PastCard, "EX1_287" });
                Check(owned.TryGetValue(PastCard, out int wild) && wild >= LocalEconomy.MaximumPlayset,
                    "StartOwned must raise a Wild-admitted card to a playset");
                Check(!owned.TryGetValue("EX1_287", out int standard) || standard == 0,
                    "StartOwned must not raise a Standard card");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // The editor's ownership gate answers for Wild-admitted cards only; Standard cards keep native ownership.
    internal static void WildStart_EditorOwnsOnlyWildCards()
    {
        Check(NativeUIProjection.WildOwnsCard(PastCard), "the editor must own a Wild-admitted card");
        Check(!NativeUIProjection.WildOwnsCard("EX1_287"), "the editor must not own an unowned Standard card");
        Check(!NativeUIProjection.WildOwnsCard(null), "the editor must not own a null card id");
    }

    // A saved Wild deck survives a reload and starts with its Past cards owned.
    internal static void WildStart_SavedDeckReloadsAndStarts()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "reload.xml");
            long id;
            using (var profile = new LocalProfile(path))
            {
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = PastCard;
                cards[1] = "EX1_008";
                profile.SaveDeck("Wild reload", "HERO_08", cards);
                id = profile.CustomDeck.Id;
                Check(profile.IsDeckComplete(id), "a saved Wild deck must be complete before reload");
            }
            using (var reloaded = new LocalProfile(path))
            {
                var deck = reloaded.GetDraft(id);
                Check(deck != null, "a saved Wild deck must reload");
                Check(deck.Cards.Count == 30, "the reloaded Wild deck keeps its cards");
                Check(reloaded.IsDeckComplete(id), "a reloaded Wild deck must stay complete");
                var owned = NativeUIProjection.StartOwned(reloaded, deck.Cards);
                Check(owned.TryGetValue(PastCard, out int past) && past >= LocalEconomy.MaximumPlayset,
                    "a reloaded Wild deck owns its Past card at start");
                Check(owned.TryGetValue("EX1_008", out int second) && second >= LocalEconomy.MaximumPlayset,
                    "a reloaded Wild deck owns every admitted card at start");
                var engineCards = deck.Cards.Select(card => SabberStoneCore.Model.Cards.FromId(card))
                    .Where(card => card != null).ToList();
                Check(SabberStoneCore.Model.OfflinePoolPolicy.Load().RequiresWildPool(engineCards),
                    "a Wild deck must derive the Wild random pool");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        WildStart_PastCardDeckIsComplete();
        WildStart_StandardDeckStillNeedsOwnership();
        WildStart_StartOwnedRaisesOnlyWildCards();
        WildStart_EditorOwnsOnlyWildCards();
        WildStart_SavedDeckReloadsAndStarts();
    }
}
