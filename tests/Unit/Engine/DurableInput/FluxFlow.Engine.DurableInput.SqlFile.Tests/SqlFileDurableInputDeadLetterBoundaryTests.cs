using System.Reflection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputDeadLetterBoundaryTests
{
    [Fact]
    public void Optional_provider_keeps_logging_out_and_sqlite_out_of_the_core_contract_assembly()
    {
        var providerReferences = typeof(SqlFileDurableInputStore)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .ToArray();
        var coreReferences = typeof(IDurableInputDeadLetterStore)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .ToArray();
        var constructor = typeof(SqlFileDurableInputStore)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .ShouldHaveSingleItem();

        providerReferences.ShouldNotContain("Microsoft.Extensions.Logging.Abstractions");
        coreReferences.ShouldNotContain("Microsoft.Data.Sqlite");
        constructor.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(SqlFileDurableInputStoreOptions)]);
    }
}
