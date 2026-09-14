using AutoHuntGrinder.Core.Localization;

namespace AutoHuntGrinder.Windows;

// A formatted label that is rebuilt only when its key or the plugin language changes, so a label drawn every frame
// allocates once per change. The key packs every input the text depends on; a build delegate should be a static
// lambda so no closure is allocated. Keep it in a mutable field or an array slot: a copy caches into itself.
internal struct CachedText
{
    private LanguageInfo? language;
    private long key;
    private string? text;

    public readonly bool TryGet(long key, out string text)
    {
        if (this.text is not null && this.key == key && ReferenceEquals(language, Loc.Current))
        {
            text = this.text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    public string Set(long key, string text)
    {
        this.key = key;
        language = Loc.Current;
        this.text = text;
        return text;
    }

    public string Get(long key, Func<long, string> build)
        => TryGet(key, out var cached) ? cached : Set(key, build(key));
}
