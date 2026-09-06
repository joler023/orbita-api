using System.Globalization;
using System.Text;
using Orbita.Domain.Common;

namespace Orbita.Domain.Tenants;

public sealed class Tenant : Entity
{
    public const int SlugMaxLength = 64;
    public const int NameMaxLength = 200;

    private Tenant(
        Guid id,
        string slug,
        string name,
        string countryCode,
        string timezone,
        string locale,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        : base(id)
    {
        Slug = slug;
        Name = name;
        CountryCode = countryCode;
        Timezone = timezone;
        Locale = locale;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string Slug { get; private set; }

    public string Name { get; private set; }

    public string CountryCode { get; private set; }

    public string Timezone { get; private set; }

    public string Locale { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Tenant Create(
        string slug,
        string name,
        string countryCode = "CO",
        string timezone = "America/Bogota",
        string locale = "es-CO",
        DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;

        return new Tenant(
            Guid.NewGuid(),
            NormalizeSlug(slug),
            RequireLength(name, nameof(name), 1, NameMaxLength),
            RequireLength(countryCode, nameof(countryCode), 2, 2),
            RequireLength(timezone, nameof(timezone), 1, 64),
            RequireLength(locale, nameof(locale), 1, 10),
            isActive: true,
            createdAt: effectiveNow,
            updatedAt: effectiveNow);
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = RequireLength(name, nameof(name), 1, NameMaxLength);
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Derives a URL-safe slug candidate from an arbitrary business name (accents,
    /// spaces and punctuation included). Unlike <see cref="NormalizeSlug"/>, which
    /// rejects invalid input, this sanitizes it — it is the building block for
    /// generating a slug during organization registration, where the caller still
    /// has to check availability and append a numeric suffix on collision.
    /// </summary>
    public static string Slugify(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > SlugMaxLength)
        {
            slug = slug[..SlugMaxLength].Trim('-');
        }

        return slug.Length == 0 ? "org" : slug;
    }

    private static string NormalizeSlug(string slug)
    {
        var trimmed = RequireLength(slug, nameof(slug), 1, SlugMaxLength).ToLowerInvariant();
        var isValidSlug = trimmed.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
        if (!isValidSlug)
        {
            throw new ArgumentException(
                "Slug must contain only letters, digits, and hyphens.",
                nameof(slug));
        }

        return trimmed;
    }

    private static string RequireLength(string value, string paramName, int minLength, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{paramName} must be between {minLength} and {maxLength} characters.",
                paramName);
        }

        return trimmed;
    }
}
