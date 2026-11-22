using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

internal sealed class MqttAccessEdits : IServiceAccessEdits
{
    internal static MqttAccessEdits Instance { get; } = new();

    public BrokerAccess Add(BrokerAccess access)
    {
        var number = 1;
        while (access.Users.Any(user => user.Name == $"user-{number}")) number++;
        var user = new BrokerUser { Name = $"user-{number}" };
        return access with { Enabled = true, Users = [.. access.Users, user],
            ClientUsername = access.Users.Count == 0 ? user.Name : access.ClientUsername };
    }

    public BrokerAccess Replace(BrokerAccess access, int index, BrokerUser user)
    {
        var previous = access.Users[index];
        if (user.Name != previous.Name && access.Users.Any(existing => existing.Name == user.Name))
            throw new ArgumentException("A broker user already has that name.");
        var users = access.Users.ToList();
        users[index] = user;
        return access with { Enabled = true, Users = users,
            ClientUsername = access.ClientUsername == previous.Name ? user.Name : access.ClientUsername };
    }

    public BrokerAccess Remove(BrokerAccess access, int index)
    {
        var users = access.Users.ToList();
        var removed = users[index];
        users.RemoveAt(index);
        return access with { Enabled = access.Enabled && users.Count > 0, Users = users,
            ClientUsername = access.ClientUsername == removed.Name ? "" : access.ClientUsername };
    }

    public BrokerAccess SelectClient(BrokerAccess access, string username) =>
        access with { Enabled = true, ClientUsername = username };

    public BrokerAccess SetCredentials(BrokerAccess access, string username, string password) =>
        access with { CustomUsername = username, CustomPassword = password };
}
