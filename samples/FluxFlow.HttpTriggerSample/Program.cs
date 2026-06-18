using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.HttpTriggerSample;
using System.Threading.Tasks.Dataflow;

var builder = WebApplication.CreateBuilder(args);

// Register the HTTP trigger as a component and wire its graph. No engine, no registry:
// the trigger gets its request source (keyed) injected and uses a request/reply
// coordinator internally; here we just link its ports to a hand-written GreetingNode.
builder.Services.AddFluxFlowHttpTrigger("greet", trigger =>
{
    var greeting = new GreetingNode();
    trigger.Output.LinkTo(greeting.Input, new DataflowLinkOptions { PropagateCompletion = false });
    greeting.Output.LinkTo(trigger.Responses, new DataflowLinkOptions { PropagateCompletion = false });
});

var app = builder.Build();

// The endpoint just feeds the keyed trigger; the coordinator writes the reply back.
app.MapFluxFlowTrigger("/greet", "greet");
app.MapGet("/", () => Results.Text("POST a name to /greet, e.g.  curl -d Ada http://localhost:5000/greet"));

app.Run();
