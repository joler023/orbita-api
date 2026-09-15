using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Media;

namespace Orbita.Api.Controllers;

/// <summary>
/// ORB-B06: the presigned-URL-style upload/download endpoints. Anonymous by design —
/// the signed token in the route *is* the authorization, the same trust model as an R2
/// presigned URL. Never reachable without a valid, unexpired, operation-matched token.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class MediaController(IMediaUrlSigner urlSigner, IMediaStorage mediaStorage) : ControllerBase
{
    [HttpGet("api/media/{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        var key = urlSigner.TryValidate(token, "get") ?? throw new InvalidMediaSignatureException();
        var stream = await mediaStorage.OpenReadAsync(key, cancellationToken);
        return stream is null ? NotFound() : File(stream, "application/octet-stream");
    }

    [HttpPut("api/media/{token}")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Put(string token, CancellationToken cancellationToken)
    {
        var key = urlSigner.TryValidate(token, "put") ?? throw new InvalidMediaSignatureException();
        await mediaStorage.SaveAsync(key, Request.Body, cancellationToken);
        return NoContent();
    }
}
