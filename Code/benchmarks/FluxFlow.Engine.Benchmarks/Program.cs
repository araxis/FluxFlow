using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance.AddJob(Job.Default.DontEnforcePowerPlan().AsMutator()));
