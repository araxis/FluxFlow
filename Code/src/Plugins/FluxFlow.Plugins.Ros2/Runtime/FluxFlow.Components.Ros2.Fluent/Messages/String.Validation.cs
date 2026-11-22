using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Plugins.Ros2.Messages.Std;

public partial class String : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (Data is null) throw new JsonException("std_msgs/msg/String.Data must be a string, not null.");
    }
}
