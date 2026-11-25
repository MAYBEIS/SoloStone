internal static class WildStartPastOwnedChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    // AT_004 is a manifest Past card: Wild admits it, the Standard ledger has no copies of it.
    private const string PastCard = "AT_004";

    // A Wild deck must start its match while its Past card still has real ledger copies of zero: Wild owns it.
    internal static void WildStart_PastCardNotOwned_StartsMatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildpast-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using (var profile = new LocalProfile(Path.Combine(root, "profile.xml")))
            {
                profile.SetPreferredFormat(true);
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = PastCard;
                profile.SaveDeck("Wild past start", "HERO_08", cards);
                long id = profile.CustomDeck.Id;
                Check(profile.OwnedCount(PastCard) == 0, "premise: the Past card has no ledger copies");
                Check(profile.PrefersWild, "premise: the profile plays Wild");
                var started = profile.Begin("custom:" + id, "priest", 1);
                Check(started != null && started.Deck == "custom:" + id, "a Wild deck with an unowned Past card must start");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // The Wild channel must stay closed for Standard: the same cards through the Standard channel are refused,
    // and an unowned Standard card still makes a Standard deck incomplete.
    internal static void WildStart_PastCardStandardStaysRefused()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildpast-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using (var profile = new LocalProfile(Path.Combine(root, "profile.xml")))
            {
                Check(!profile.PrefersWild, "premise: this profile plays Standard");
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = PastCard;
                bool refused = false;
                try { LocalShop.ValidateDeck("Standard past", "HERO_08", cards, null, true, false, true, false); }
                catch (InvalidOperationException) { refused = true; }
                Check(refused, "the Standard channel must refuse a Past card");
                var standard = LocalDecks.Get("mage").CardIds;
                standard[0] = "EX1_287";
                profile.SaveDeck("Standard unowned", "HERO_08", standard);
                Check(!profile.IsDeckComplete(profile.CustomDeck.Id), "a Standard deck with an unowned card stays incomplete");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        WildStart_PastCardNotOwned_StartsMatch();
        WildStart_PastCardStandardStaysRefused();
    }
}
