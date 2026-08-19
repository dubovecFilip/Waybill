namespace Waybill;

/// <summary>
/// UI text in every language the window speaks. Plain dictionaries rather than
/// .resx files: everything is readable as ordinary code, and switching language
/// at runtime is a field assignment plus a redraw.
///
/// One file per language, all of them parts of this class. Adding a language
/// means a new file, a line in <see cref="All"/> and a line in
/// <see cref="Table"/>.
/// </summary>
public static partial class Strings {
    public static string Language = "en";

    /// <summary>The languages offered, in the order the menu lists them. The name is
    /// written in the language itself, because someone looking for their own
    /// language is not reading the current one.</summary>
    public static readonly (string Code, string Name)[] All = {
        ("en", "English"),
        ("sk", "Slovenčina"),
        ("cs", "Čeština"),
        ("de", "Deutsch"),
        ("es", "Español"),
    };

    /// <summary>
    /// The table for a language code.
    ///
    /// A switch rather than a dictionary of dictionaries, because the tables live in
    /// other files now and C# only promises to run static field initialisers in
    /// order within one file. A dictionary built here could be built before the
    /// tables it holds and end up full of nulls; a switch reads them when asked, by
    /// which time everything exists.
    /// </summary>
    private static Dictionary<string, string> Table(string code) => code switch {
        "sk" => Slovak,
        "cs" => Czech,
        "de" => German,
        "es" => Spanish,
        _ => English,
    };

    /// <summary>English is the fallback because the rest of the project is English:
    /// the code, the documentation and the stored identifiers. A key missing from a
    /// translation shows the English wording rather than a blank or a key.</summary>
    public static string T(string key) {
        if (Table(Language).TryGetValue(key, out var text)) return text;
        return English.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
