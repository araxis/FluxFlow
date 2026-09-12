using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

if (args.FirstOrDefault() == "--load")
{
    await FluxFlow.Engine.Benchmarks.LoadQualification.LoadCommand.RunAsync(args[1..]);
    return;
}

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance.AddJob(Job.Default.DontEnforcePowerPlan().AsMutator()));
