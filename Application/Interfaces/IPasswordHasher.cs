namespace Application.Interfaces;

// Seam for password hashing: swap the implementation in DI (e.g. for ASP.NET Identity's PasswordHasher).
public interface IPasswordHasher
{
    public string Hash(string password);
    public bool Verify(string password, string storedHash);
}
