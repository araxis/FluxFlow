namespace FluxFlow.Environments;

public sealed record BrokerAccess
{
    public bool Enabled { get; init; }
    public List<BrokerUser> Users { get; init; } = [];
    public string ClientUsername { get; init; } = "";
    public string CustomUsername { get; init; } = "";
    public string CustomPassword { get; init; } = "";

    public void Validate()
    {
        if (!Enabled) return;
        if (Users is null || Users.Count == 0 || Users.Any(user => user is null ||
            string.IsNullOrWhiteSpace(user.Name) || user.Name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ':') ||
            string.IsNullOrEmpty(user.Password) || user.Password.Contains('\0') || user.Read is null || user.Write is null))
            throw new ArgumentException("Each broker user requires a name without spaces or colons, a password and permission fields.");
        if (Users.Select(user => user.Name).Distinct(StringComparer.Ordinal).Count() != Users.Count)
            throw new ArgumentException("Broker usernames must be unique.");
        if (!Users.Any(user => user.Name == ClientUsername))
            throw new ArgumentException("Select the broker user for this workflow connection.");
    }
}

public sealed record BrokerUser
{
    public string Name { get; init; } = "";
    public string Password { get; init; } = "";
    public string Read { get; init; } = "";
    public string Write { get; init; } = "";
}
