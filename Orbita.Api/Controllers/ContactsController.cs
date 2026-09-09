using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbita.Api.Identity;
using Orbita.Application.Crm;

namespace Orbita.Api.Controllers;

/// <summary>ORB-D02/D03: contacts, custom fields, and trigram search. Conversation history is Track B.</summary>
[ApiController]
[Authorize]
public sealed class ContactsController(IContactService contactService) : ControllerBase
{
    [HttpGet("api/tenants/{tenantId:guid}/contacts")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactListItem>>> Search(
        Guid tenantId,
        [FromQuery] string? q,
        [FromQuery] string? channel,
        CancellationToken cancellationToken)
    {
        return Ok(await contactService.SearchAsync(tenantId, User.GetUserId(), q, channel, cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/contacts")]
    [ProducesResponseType(typeof(ContactDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContactDetail>> Create(
        Guid tenantId,
        [FromBody] CreateContactRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await contactService.CreateAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return Created($"/api/tenants/{tenantId}/contacts/{contact.Id}", contact);
    }

    [HttpGet("api/tenants/{tenantId:guid}/contacts/{contactId:guid}")]
    [ProducesResponseType(typeof(ContactDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactDetail>> Get(Guid tenantId, Guid contactId, CancellationToken cancellationToken)
    {
        return Ok(await contactService.GetAsync(tenantId, User.GetUserId(), contactId, cancellationToken));
    }

    [HttpPatch("api/tenants/{tenantId:guid}/contacts/{contactId:guid}")]
    [ProducesResponseType(typeof(ContactDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContactDetail>> Update(
        Guid tenantId,
        Guid contactId,
        [FromBody] UpdateContactRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await contactService.UpdateAsync(tenantId, User.GetUserId(), contactId, request, cancellationToken));
    }

    [HttpGet("api/tenants/{tenantId:guid}/contacts/{contactId:guid}/opportunities")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactOpportunitySummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactOpportunitySummary>>> ListOpportunities(
        Guid tenantId,
        Guid contactId,
        CancellationToken cancellationToken)
    {
        return Ok(await contactService.ListOpportunitiesAsync(tenantId, User.GetUserId(), contactId, cancellationToken));
    }

    [HttpGet("api/tenants/{tenantId:guid}/contact-fields")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactFieldDefinitionSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactFieldDefinitionSummary>>> ListFields(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return Ok(await contactService.ListFieldsAsync(tenantId, User.GetUserId(), cancellationToken));
    }

    [HttpPost("api/tenants/{tenantId:guid}/contact-fields")]
    [ProducesResponseType(typeof(ContactFieldDefinitionSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContactFieldDefinitionSummary>> CreateField(
        Guid tenantId,
        [FromBody] CreateContactFieldRequest request,
        CancellationToken cancellationToken)
    {
        var field = await contactService.CreateFieldAsync(tenantId, User.GetUserId(), request, cancellationToken);
        return Created($"/api/tenants/{tenantId}/contact-fields/{field.Id}", field);
    }
}
