internal static class WildOwnershipChecks
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
    // only the Wild pool draws it. Cards such as EX1_029 sit in the ledger already, so they cannot prove the channel.
    private const string PastCard = "AT_004";

    // Wild mode owns the manifest's Past cards through its own admission channel, parallel to the Standard ledger.
    internal static void WildOwnership_PastCardCanEnterDeck()
    {
        Check(LocalProfile.IsWildAdmittedCard(PastCard), "AT_004 must be a manifest Past card");
        Check(!LocalProfile.KnownLedgerCard(PastCard), "AT_004 must stay outside the Standard ledger");
        var wild = LocalProfile.LedgerDeck("Wild draft", "HERO_08", new[] { PastCard }, null, false);
        Check(wild.CardIds.Contains(PastCard), "Wild deck must admit a Past card");
    }

    // Standard mode keeps today's behaviour: the same card is still refused, so the Wild channel is not a widening
    // of the Standard rules.
    internal static void WildOwnership_StandardModeUnchanged()
    {
        Check(!LocalProfile.KnownLedgerCard(PastCard), "AT_004 must stay outside the Standard ledger");
        Reject(() => LocalShop.ValidateDeck("Standard draft", "HERO_08", new[] { PastCard }, null, false),
            "Standard deck must still reject the same Past card");
    }

    // A saved Wild deck must load again: the stored-draft path knows no Past card, so the reload check has to take
    // the same Wild channel the save took.
    internal static void WildOwnership_SavedDeckSurvivesReload()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wild-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "wild.xml");
            using (var profile = new LocalProfile(path))
            {
                var cards = LocalDecks.Get("mage").CardIds;
                cards[0] = PastCard;
                profile.SaveDeck("Wild draft", "HERO_08", cards);
                Check(profile.CustomDeck.Cards.Contains(PastCard), "Wild deck must save a Past card");
            }
            using (var profile = new LocalProfile(path))
                Check(profile.CustomDeck.Cards.Contains(PastCard), "Wild deck must survive reload");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        WildOwnership_PastCardCanEnterDeck();
        WildOwnership_StandardModeUnchanged();
        WildOwnership_SavedDeckSurvivesReload();
    }
}
