namespace Api.Modules.Users;

public sealed class BrokerProfile
{
    public Guid UserId { get; set; }

    /// <summary>Free text — placeholder semantics until #15 (the IRIS code list) is answered.</summary>
    public required string IrisCode { get; set; }

    public required string Email { get; set; }
}
