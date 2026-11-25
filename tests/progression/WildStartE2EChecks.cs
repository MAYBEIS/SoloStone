using System.Reflection;
using System.Xml.Serialization;
using SabberStoneCore.Model;

internal static class WildStartE2EChecks
{
    private static readonly XmlSerializer Serializer = new(typeof(LocalProfileData));
    private static readonly MethodInfo ValidateOwnership = typeof(LocalProfile).GetMethod("ValidateDeckOwnership",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    // AT_004 is a manifest Past card outside the Standard ledger; Wild admits it, Standard does not.
    private const string PastCard = "AT_004";

    // EX1_287 is an unowned Standard card: the Standard channel must still refuse it.
    private const string UnownedStandardCard = "EX1_287";

    // The refusal NativeUIProjection.ValidateStart raises when IsDeckComplete answers false.
    private const string IncompleteRefusal = "The saved deck is incomplete. Acquire missing cards before playing.";

    private static string[] WildCards() { var cards = LocalDecks.Get("mage").CardIds; cards[0] = PastCard; return cards.ToArray(); }
    private static string[] StandardCards() { var cards = LocalDecks.Get("mage").CardIds; cards[0] = UnownedStandardCard; return cards.ToArray(); }
    private static LocalProfileData Read(string path) { using var stream = File.OpenRead(path); return (LocalProfileData)Serializer.Deserialize(stream); }

    // The start check's last step is private; a refusal here is exactly what makes IsDeckComplete answer false.
    private static bool OwnershipRefused(LocalProfile profile, LocalCustomDeck deck, bool wild)
    {
        try { ValidateOwnership.Invoke(profile, new object[] {
            deck.CardEntries.Concat(deck.Sideboards.SelectMany(board => board.CardEntries)), true, wild }); return false; }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return true; }
    }

    // The start path can only refuse through IsDeckComplete, so that must answer false before the refusal below means anything.
    private static void CheckStartRefused(LocalProfile profile, LocalCustomDeck saved, string expected)
    {
        Check(!profile.IsDeckComplete(saved.Id), "premise: IsDeckComplete must refuse the deck before ValidateStart");
        var resolved = profile.ResolveCustomDeck();
        Check(resolved != null, "premise: the saved deck resolves");
        try { NativeUIProjection.ValidateStart(profile, "custom:" + saved.Id, resolved, true); }
        catch (InvalidOperationException error) when (error.Message == expected) { return; }
        throw new Exception("ValidateStart must refuse with: " + expected);
    }

    // A saved Wild deck (a Past card included) walks the whole start check: IsDeckComplete ->
    // ValidateDeckOwnership(wild: true) -> LocalCrafting.MissingDeckSlots, then ValidateStart accepts it.
    internal static void WildStartE2E_PastCardDeckStarts()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "wild.xml");
            using (var profile = new LocalProfile(path))
            {
                profile.SaveDeck("Wild e2e", "HERO_08", WildCards());
                var saved = profile.CustomDeck;
                Check(saved != null, "premise: the Wild deck saved");
                Check(saved.Cards.Contains(PastCard), "premise: the Wild deck carries the Past card");
                // The fixture must cost exactly its Past card: the plain ledger refuses it, the Wild channel does not.
                Check(OwnershipRefused(profile, saved, false), "premise: the Standard ledger refuses the Past card");
                Check(!OwnershipRefused(profile, saved, true), "premise: the Wild channel owns every other copy");
                Check(profile.IsDeckComplete(saved.Id), "a Wild deck with a Past card must be complete");
                var resolved = profile.ResolveCustomDeck();
                Check(resolved != null, "a Wild deck must resolve to a playable deck");
                var entries = NativeUIProjection.ValidateStart(profile, "custom:" + saved.Id, resolved, true);
                Check(entries != null && entries.Count == 30, "a Wild deck must pass the start check with its 30 copies");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // Standard keeps today's rule end to end: an unowned Standard card fails IsDeckComplete and refuses the
    // ownership step without the Wild channel, so ValidateStart answers with the same incomplete message.
    internal static void WildStartE2E_StandardDeckStillRefused()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "standard.xml");
            using (var profile = new LocalProfile(path))
            {
                profile.SaveDeck("Standard e2e", "HERO_08", StandardCards());
                var saved = profile.CustomDeck;
                Check(saved != null, "premise: the Standard deck saved");
                Check(saved.Cards.Contains(UnownedStandardCard), "premise: the Standard deck carries the unowned card");
                Check(OwnershipRefused(profile, saved, false), "an unowned Standard card still refuses the ownership step");
                Check(OwnershipRefused(profile, saved, true), "the Wild channel does not own an unowned Standard card");
                Check(!profile.IsDeckComplete(saved.Id), "a Standard deck with an unowned card must stay incomplete");
                CheckStartRefused(profile, saved, IncompleteRefusal);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // Guardian for the Wild gate itself: MissingDeckSlots is the last step of the start check, so a Past card
    // costs the Wild deck no slot while it still costs the plain Standard ledger both copies.
    internal static void WildStartE2E_WildGateOwnsPastCardsOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "guard.xml");
            LocalCustomDeck guard;
            using (var profile = new LocalProfile(path))
            {
                profile.SaveDeck("Wild guard", "HERO_08", WildCards());
                guard = profile.GetDraft(profile.CustomDeck.Id);
                Check(guard != null, "premise: the Wild deck reloads");
                Check(OwnershipRefused(profile, guard, false), "the Standard ledger refuses the Past card");
                Check(!OwnershipRefused(profile, guard, true), "the Wild channel owns the Past card");
            }
            var data = Read(path);
            var ledger = LocalProfile.DeckOwnedCount(data);
            Check(ledger(PastCard, 0) == 0, "premise: the Past fixture is unowned in the Standard ledger");
            Check(LocalCrafting.MissingDeckSlots(guard, LocalProfile.WildDeckOwnedCount(ledger)) == 0,
                "under the Wild totals a Past card costs no slot");
            Check(LocalCrafting.MissingDeckSlots(guard, ledger) > 0,
                "without the Wild channel the same deck misses its Past card");
            Check(LocalProfile.WildDeckOwnedCount(ledger)(UnownedStandardCard, 0) == 0,
                "the Wild totals leave an unowned Standard card at zero");
        }
        finally { Directory.Delete(root, true); }
    }

    // Import (NativeDeckCodeHooks LastImportMissing) and diagnostics (NativeTestControl deckOwnership.missing) read
    // MissingDeckSlotCount, so that count must use the same Wild channel as the start check: a Wild deck's Past card
    // costs no slot, while an unowned Standard card is still charged.
    internal static void WildStartE2E_MissingSlotCountUsesWildChannel()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "wild-slots.xml");
            using (var profile = new LocalProfile(path))
            {
                profile.SaveDeck("Wild slots", "HERO_08", WildCards());
                var saved = profile.CustomDeck;
                Check(saved != null, "premise: the Wild deck saved");
                Check(saved.Cards.Contains(PastCard), "premise: the Wild deck carries the Past card");
                // Premise: the plain ledger does charge the Past card, so the Wild channel is what frees the deck.
                Check(LocalCrafting.MissingDeckSlots(saved, profile.OwnedCount) > 0,
                    "premise: the plain ledger charges the Past card");
                Check(profile.MissingDeckSlotCount(saved.Id) == 0,
                    "a Wild deck must cost no slot for its Past card");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    // The Wild channel must not mask a real gap: an unowned Standard card keeps costing its slot, exactly as the
    // plain ledger charges it, so Standard imports and diagnostics stay truthful.
    internal static void WildStartE2E_MissingSlotCountStandardGapStays()
    {
        string root = Path.Combine(Path.GetTempPath(), "hs-wildstart-e2e-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "standard-slots.xml");
            using (var profile = new LocalProfile(path))
            {
                profile.SaveDeck("Standard slots", "HERO_08", StandardCards());
                var saved = profile.CustomDeck;
                Check(saved != null, "premise: the Standard deck saved");
                Check(saved.Cards.Contains(UnownedStandardCard), "premise: the Standard deck carries the unowned card");
                int plain = LocalCrafting.MissingDeckSlots(saved, profile.OwnedCount);
                Check(plain > 0, "premise: the plain ledger charges the unowned Standard card");
                Check(profile.MissingDeckSlotCount(saved.Id) == plain,
                    "the Wild channel must not mask an unowned Standard card");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        WildStartE2E_PastCardDeckStarts();
        WildStartE2E_StandardDeckStillRefused();
        WildStartE2E_WildGateOwnsPastCardsOnly();
        WildStartE2E_MissingSlotCountUsesWildChannel();
        WildStartE2E_MissingSlotCountStandardGapStays();
    }
}
