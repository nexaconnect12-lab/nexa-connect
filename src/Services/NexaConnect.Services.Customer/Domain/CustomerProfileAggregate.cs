namespace NexaConnect.Services.Customer.Domain;

public sealed class CustomerIdempotencyConflictException(string message) : InvalidOperationException(message);

public sealed class CustomerProfileAggregate
{
    private CustomerProfileAggregate(
        Guid id,
        Guid organizationId,
        string customerNumber,
        string displayName,
        string? identitySubjectId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerNumber = customerNumber;
        DisplayName = displayName;
        IdentitySubjectId = identitySubjectId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string CustomerNumber { get; }
    public string DisplayName { get; }
    public string? IdentitySubjectId { get; }
    public string Status => "active";
    public long ConcurrencyVersion => 1;
    public DateTimeOffset CreatedAtUtc { get; }

    public static CustomerProfileAggregate Create(
        Guid organizationId,
        string customerNumber,
        string displayName,
        string? identitySubjectId,
        DateTimeOffset createdAtUtc)
    {
        string number = NormalizeRequired(customerNumber, 100, "Customer number");
        string name = NormalizeRequired(displayName, 200, "Display name");
        string? subject = NormalizeOptional(identitySubjectId, 200, "Identity subject");
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.");

        return new CustomerProfileAggregate(Guid.NewGuid(), organizationId, number, name, subject, createdAtUtc);
    }

    private static string NormalizeRequired(string value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{field} is required.");
        string normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
            throw new ArgumentException($"{field} is invalid.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
            throw new ArgumentException($"{field} is invalid.");
        return normalized;
    }
}
