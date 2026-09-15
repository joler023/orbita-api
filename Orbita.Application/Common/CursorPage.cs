namespace Orbita.Application.Common;

/// <summary>
/// One page of a list, with a cursor for the next one.
///
/// This shape (<c>{ items, nextCursor }</c>) is the contract agreed with the frontend for
/// every list in the product. Keyset pagination rather than offset because offset paging
/// degrades exactly where it hurts most — deep into a long list — and ORB-B12 already
/// requires the inbox to stay under a second with 10.000 conversations.
///
/// It ships here with ORB-C02's knowledge-document list, which is the first list long
/// enough to need paging.
/// </summary>
/// <param name="Items">This page, in the query's order.</param>
/// <param name="NextCursor">
/// Opaque token to pass back for the following page, or null when this is the last one.
/// Callers must treat it as a blob: its encoding is free to change.
/// </param>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public static CursorPage<T> Empty { get; } = new([], null);
}
