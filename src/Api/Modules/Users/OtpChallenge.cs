namespace Api.Modules.Users;

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public required string Phone { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
