using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.HttpTriggerSample;
using System.Threading.Tasks.Dataflow;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Compose the graph by hand — no engine, no registry:
//   HTTP request --(trigger)--> bridge --> GreetingNode --> bridge --> HTTP response
var bridge = new RequestReplyBridge<HttpTriggerRequest, HttpTriggerReply>();
var greeting = new GreetingNode();
bridge.Output.LinkTo(greeting.Input, new DataflowLinkOptions { PropagateCompletion = false });
greeting.Output.LinkTo(bridge.Responses, new DataflowLinkOptions { PropagateCompletion = false });

// Tidy shutdown.
app.Lifetime.ApplicationStopping.Register(() => bridge.Complete());

// Expose the graph as an endpoint. The bridge correlates the reply back to the caller.
app.MapFluxFlowTrigger("/greet", bridge);
app.MapGet("/", () => Results.Text("POST a name to /greet, e.g.  curl -d Ada http://localhost:5000/greet"));

app.Run();
