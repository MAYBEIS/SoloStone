using System.Xml.Serialization;

// Wild format preference: the native picker's Wild choice is remembered per save, next to the Standard/Casual
// play mode. Wild is a deck format, not a match mode, so it never reuses LocalProfileData.PlayMode (a "Wild"
// mode stays an unknown match mode).
internal static class WildFormatChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is IOException) { return; }
        throw new Exception("Expected rejection: " + message);
    }

    internal static void Run(string root)
    {
        string path = Path.Combine(root, "wild-format.xml");
        using (var p = new LocalProfile(path))
        {
            Check(!p.PrefersWild, "Standard is the default deck format");
            p.SetPreferredFormat(true);
            Check(p.PrefersWild, "Wild is remembered in the running profile");
        }
        using (var p = new LocalProfile(path))
        {
            Check(p.PrefersWild, "Wild survives reload");
            p.SetPreferredFormat(false);
            Check(!p.PrefersWild, "format switches back to Standard");
        }

        // A Wild format never becomes a match mode: the mode field keeps rejecting it.
        var saved = Read(path);
        string control = Path.Combine(root, "wild-format-control.xml");
        Write(control, LocalProfile.Clone(saved));
        using (var p = new LocalProfile(control)) Check(!p.PrefersWild, "untampered rewrite loads (tamper control)");
        foreach (var (name, tamper) in new (string, Action<LocalProfileData>)[] {
            ("unknown deck format", d => d.DeckFormat = "Classic"),
            ("Wild as a match mode", d => d.PlayMode = "Wild"),
        })
        {
            string copy = Path.Combine(root, "wild-format-tamper.xml");
            var data = LocalProfile.Clone(saved);
            tamper(data);
            Write(copy, data);
            Reject(() => new LocalProfile(copy).Dispose(), name);
        }

        // A Standard-only save writes no format element (old saves stay byte-identical).
        string standardPath = Path.Combine(root, "wild-format-standard.xml");
        using (var p = new LocalProfile(standardPath)) { }
        string xml = File.ReadAllText(standardPath);
        Check(!xml.Contains("<DeckFormat"), "a Standard save writes no deck format element");
    }

    static LocalProfileData Read(string path) { using var s = File.OpenRead(path); return (LocalProfileData)new XmlSerializer(typeof(LocalProfileData)).Deserialize(s); }
    static void Write(string path, LocalProfileData p) { using var s = File.Create(path); new XmlSerializer(typeof(LocalProfileData)).Serialize(s, p); }
}
