namespace Domain.Users;

// Domain service interface: Password asks it to hash; the implementation (PBKDF2) lives in Infrastructure
// and can be swapped in DI (e.g. for ASP.NET Identity's PasswordHasher).
public interface IPasswordHasher
{
    public string Hash(string password);
    public bool Verify(string password, string storedHash);
}
