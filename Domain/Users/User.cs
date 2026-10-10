namespace Domain.Users;

public class User
{
    public int Id { get; set; }
    public Email Email { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }

    private User(Email email, string passwordHash)
    {
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = DateTime.UtcNow;
    }

    private User(int userId, Email email, string passwordHash, DateTime createdAt)
    {
        Id = userId;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }
    
    public static User Create(int userId, Email email, string passwordHash, DateTime createdAt) => new User(email, passwordHash);
    public static User Create(Email email, string passwordHash) => new User(email, passwordHash);
}
