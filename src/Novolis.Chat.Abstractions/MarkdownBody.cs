namespace Novolis.Chat.Abstractions;

/// <summary>
/// The wire-level body contract for chat. A source containing no Markdown
/// markers is still valid Markdown; rendering is a consumer concern.
/// </summary>
public readonly record struct MarkdownBody(string Source)
{
    public static MarkdownBody FromRaw(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MarkdownBody(source);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Source);

    public override string ToString() => Source;
}

public enum ChatBodyFormat
{
    Markdown = 0,
}
