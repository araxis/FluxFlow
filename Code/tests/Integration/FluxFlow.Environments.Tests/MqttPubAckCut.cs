using System.Net;
using System.Net.Sockets;

namespace FluxFlow.Environments.Tests;

// Loopback relay for a single subscriber. Arm only after SUBACK, with one ACK command pending.
internal sealed class MqttPubAckCut : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop;
    private readonly CancellationTokenSource _first;
    private readonly Task _run;
    private readonly TaskCompletionSource _intercepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _armed;
    private volatile bool _allowRecovery;

    public MqttPubAckCut(Uri broker, bool forwardWholeAck, CancellationToken token)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        _first = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _listener.Start();
        _run = RunAsync(broker, forwardWholeAck);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public void Arm() => _armed = true;
    public Task Intercepted => Task.WhenAny(_intercepted.Task, _run).Unwrap();
    public void Cut() => _first.Cancel();
    public void AllowRecovery() => _allowRecovery = true;

    private async Task RunAsync(Uri broker, bool forwardWholeAck)
    {
        using (var first = await _listener.AcceptTcpClientAsync(_stop.Token))
            await Relay(first, true, _first.Token);
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            if (_allowRecovery) await Relay(client, false, _stop.Token);
        }

        async Task Relay(TcpClient client, bool firstConnection, CancellationToken token)
        {
            using var upstream = new TcpClient();
            await upstream.ConnectAsync(broker.Host, broker.Port, token);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            var outgoing = client.GetStream();
            var incoming = upstream.GetStream();
            var sent = firstConnection ? ForwardUntilCut(outgoing, incoming, connection.Token) : outgoing.CopyToAsync(incoming, connection.Token);
            var received = incoming.CopyToAsync(outgoing, connection.Token);
            await Task.WhenAny(sent, received);
            connection.Cancel();
            try { await Task.WhenAll(sent, received); }
            catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
        }

        async Task ForwardUntilCut(NetworkStream client, NetworkStream server, CancellationToken token)
        {
            var buffer = new byte[4096];
            int count;
            while ((count = await client.ReadAsync(buffer, token)) > 0)
            {
                if (!_armed || _intercepted.Task.IsCompleted)
                {
                    await server.WriteAsync(buffer.AsMemory(0, count), token);
                    continue;
                }
                if (buffer[0] != 0x40) throw new InvalidDataException($"Expected PUBACK, received 0x{buffer[0]:X2}.");
                await server.WriteAsync(buffer.AsMemory(0, forwardWholeAck ? count : 1), token);
                _intercepted.SetResult();
                if (!forwardWholeAck) await Task.Delay(Timeout.Infinite, token);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _run; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            _listener.Stop();
            _first.Dispose();
            _stop.Dispose();
        }
    }
}
