namespace Orbita.Domain.Ai;

/// <summary>
/// Where a knowledge document is in the indexing pipeline — orbita-schema.dbml's
/// <c>knowledge_doc_status</c> enum.
///
/// ORB-C02 requires this to be visible to the user: indexing is asynchronous and a
/// 50-page document can take up to two minutes, so a screen that shows nothing makes
/// people think the upload broke.
/// </summary>
public enum KnowledgeDocStatus
{
    /// <summary>Uploaded and queued; no work started yet.</summary>
    Pending,

    /// <summary>Text is being extracted, chunked and embedded right now.</summary>
    Processing,

    /// <summary>Every chunk is embedded and searchable.</summary>
    Indexed,

    /// <summary>Something went wrong; <c>KnowledgeDocument.FailureReason</c> says what, in the user's language.</summary>
    Failed,
}
