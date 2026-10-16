using Domain.Datatypes;

namespace Domain.Accounts;

public class Account
{
    public int Id { get; set; }
    // foriegn key into user table
    public int UserId { get; set; }
    public Amount Balance { get; set; }
    public Currency Currency { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    private Account(int id, int userId, Amount balance, Currency currency, DateTime createdAt, DateTime updatedAt)
    {
        Id = id;
        UserId = userId;
        Balance = balance;
        Currency = currency;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private Account(int userId, Amount balance, Currency currency, DateTime createdAt, DateTime updatedAt)
    {
        UserId = userId;
        Balance = balance;
        Currency = currency;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static Account Create(int id, int userId, Amount balance, Currency currency, DateTime createdAt, DateTime updatedAt)
    {
        return new Account(id, userId, balance, currency, createdAt, updatedAt);
    }

    public static Account Create(int userId, Amount balance, Currency currency, DateTime createdAt, DateTime updatedAt)
    {
        return new Account(userId, balance, currency, createdAt, updatedAt);
    }
}
