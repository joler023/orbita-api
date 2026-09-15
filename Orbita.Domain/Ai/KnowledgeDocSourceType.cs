namespace Orbita.Domain.Ai;

/// <summary>
/// How a knowledge document got here — orbita-schema.dbml's <c>source_type</c>
/// (<c>upload | url | manual</c>).
/// </summary>
public enum KnowledgeDocSourceType
{
    /// <summary>A PDF, DOCX, TXT or Markdown file the user uploaded.</summary>
    Upload,

    /// <summary>Text pasted straight into the form — no original file exists.</summary>
    Manual,

    /// <summary>Fetched from a URL. Not implemented yet; the column allows it because the DBML does.</summary>
    Url,
}
