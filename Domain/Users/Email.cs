namespace Domain.Users;

public sealed record Email
{
    public string EmailAddress { get; }

    private Email(string emailAddress)
    {
        EmailAddress = emailAddress;
    }

    public static Email Create(string emailAddress) => new(Normalize(emailAddress));

    private static string Normalize(string emailAddress) => emailAddress.Trim().ToLowerInvariant();
}