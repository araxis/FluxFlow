using System.Runtime.CompilerServices;

// The document boundary needs to avoid disposing an instance already owned by another graph.
[assembly: InternalsVisibleTo("FluxFlow.Json")]
