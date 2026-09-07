namespace Orbita.Application.Ai;

/// <summary>
/// Where the original uploaded file lives.
///
/// CLAUDE.md's rule 7 keeps media out of Postgres: documents are referenced by an object
/// key and the bytes live elsewhere. orbita-schema.dbml expects that to be Cloudflare R2;
/// R2 is not provisioned yet, so the shipped implementation writes to local disk — the
/// same documented stand-in pattern as logging invitation emails instead of sending them.
/// Swapping in R2 later means writing one class, not touching this port.
///
/// Keys are namespaced by tenant so a signed URL can never be made to point outside the
/// tenant that owns the file.
/// </summary>
public interface IKnowledgeDocumentStorage
{
    /// <returns>The storage key to persist in <c>knowledge_docs.source_ref</c>.</returns>
    Task<string> SaveAsync(Guid tenantId, string fileName, Stream content, CancellationToken cancellationToken);

    /// <exception cref="FileNotFoundException">The key does not resolve — e.g. the file was removed out of band.</exception>
    Task<Stream> OpenAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Best-effort: a key that is already gone is not an error.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
