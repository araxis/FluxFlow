using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;
using Shouldly;
using Xunit;

namespace FluxFlow.ReportingSample.Tests;

public sealed class ReportingCatalogTests
{
    [Fact]
    public void Catalog_discovers_arbitrary_reports_and_freezes_custom_metadata()
    {
        var fields = new List<ApplicationReportFieldDescriptor> { Counter("process") };
        var report = new TestReport(new() { Type = "orders.total", Category = "business", Description = "Orders", Fields = fields });
        var custom = new List<IApplicationReport>
        {
            report,
            new TestReport(new() { Type = "inventory.total", Description = "Inventory" })
        };
        var catalog = new ApplicationReportCatalog(custom);
        fields.Clear();
        custom.Clear();

        var result = catalog.Descriptors.Single(d => d.Type == "orders.total");
        result.Fields.Single().ResetScope.ShouldBe("process");
        result.Fields.Single().Unit.ShouldBe("messages");
        result.Category.ShouldBe("business");
        catalog.Reports.Single(item => item.Descriptor.Type == "orders.total").ShouldBeSameAs(report);
        catalog.Descriptors.Select(d => d.Type).ShouldBe(["inventory.total", "orders.total"]);
        catalog.Descriptors.Count.ShouldBe(2);
    }

    [Fact]
    public void Catalog_allows_new_versions_but_rejects_duplicate_identity_and_field_paths()
    {
        var descriptor = new ApplicationReportDescriptor { Type = "orders.total", Description = "Orders", Fields = [Counter("process")] };
        Catalog(descriptor, descriptor with { Version = 2 }).Descriptors.Count(d => d.Type == descriptor.Type).ShouldBe(2);
        Should.Throw<ArgumentException>(() => Catalog(descriptor, descriptor));
        Should.Throw<ArgumentException>(() => Catalog(descriptor with { Version = 0 }));
        Should.Throw<ArgumentException>(() => Catalog(descriptor with { Fields = [Counter("process"), Counter("process")] }));
    }

    [Theory]
    [InlineData(null, ApplicationReportFieldKind.Number)]
    [InlineData("", ApplicationReportFieldKind.Number)]
    [InlineData("process", ApplicationReportFieldKind.String)]
    public void Cumulative_measurements_require_numeric_fields_and_reset_scope(string? resetScope, ApplicationReportFieldKind kind)
        => Should.Throw<ArgumentException>(() => Catalog(new ApplicationReportDescriptor
        { Type = "orders.total", Description = "Orders", Fields = [Counter(resetScope) with { Kind = kind }] }));

    [Fact]
    public void Comparison_discovery_exposes_only_operations_supported_by_field_kind()
    {
        var catalog = new ApplicationReportCatalog();
        catalog.GetComparisons(ApplicationReportFieldKind.Number).ShouldContain(ApplicationReportComparison.GreaterThan);
        catalog.GetComparisons(ApplicationReportFieldKind.Number).ShouldNotContain(ApplicationReportComparison.Contains);
        catalog.GetComparisons(ApplicationReportFieldKind.String).ShouldContain(ApplicationReportComparison.StartsWith);
        catalog.GetComparisons(ApplicationReportFieldKind.Boolean).ShouldNotContain(ApplicationReportComparison.GreaterThan);
        catalog.GetComparisons(ApplicationReportFieldKind.Object).ShouldBe(new[]
        { ApplicationReportComparison.Exists, ApplicationReportComparison.Missing, ApplicationReportComparison.IsNull });
    }

    [Fact]
    public void Common_fields_discover_envelope_and_event_fields_with_usable_types()
    {
        var catalog = new ApplicationReportCatalog();
        catalog.CommonFields.Select(field => field.Path).ShouldBe(new[]
        {
            "/type", "/kind", "/level", "/timestamp", "@application", "@revision", "@schemaVersion",
            "@phase", "@source", "@workflow", "@component", "@traceId"
        });
        catalog.CommonFields.Single(field => field.Path == "@schemaVersion").Kind.ShouldBe(ApplicationReportFieldKind.Number);
        catalog.CommonFields.Single(field => field.Path == "/timestamp").Kind.ShouldBe(ApplicationReportFieldKind.Timestamp);
        catalog.CommonFields.Single(field => field.Path == "@revision").Kind.ShouldBe(ApplicationReportFieldKind.String);
        foreach (var field in catalog.CommonFields)
            Should.NotThrow(() => ReportingFilterTests.Filter(field.Path, ApplicationReportComparison.Exists).Compile());
    }

    [Fact]
    public async Task Report_registration_exposes_same_singleton_and_plain_interface_registration_extends_catalog()
    {
        var services = new ServiceCollection();
        services.AddApplicationReport<CustomOrderReport>();
        services.AddSingleton<IApplicationReport, CustomAuditReport>();
        services.AddSingleton<ApplicationReportCatalog>();
        await using var provider = services.BuildServiceProvider();
        var report = provider.GetRequiredService<CustomOrderReport>();
        var all = provider.GetServices<IApplicationReport>().ToArray();
        all.OfType<CustomOrderReport>().Single().ShouldBeSameAs(report);
        var catalog = provider.GetRequiredService<ApplicationReportCatalog>();
        catalog.Reports.Single(r => r.Descriptor.Type == "custom.orders").ShouldBeSameAs(report);
        catalog.Descriptors.Single(d => d.Type == "custom.orders").Category.ShouldBe("business");
        catalog.Descriptors.Single(d => d.Type == "custom.audit").Category.ShouldBe("audit");
        catalog.Descriptors.Select(descriptor => descriptor.Type).ShouldBe(["custom.audit", "custom.orders"]);
        var record = ReportingFilterTests.Record("{}") with { Message = Measurement(10) };
        report.Includes(record).ShouldBeTrue();
        report.Includes(record with { Message = Measurement(1) }).ShouldBeFalse();
        var source = new BufferBlock<ApplicationReportEvent>();
        var selected = new BufferBlock<ApplicationReportEvent>();
        using var link = source.LinkTo(selected, new DataflowLinkOptions(), report.Includes);
        using var discard = source.LinkTo(DataflowBlock.NullTarget<ApplicationReportEvent>());
        source.Post(record with { Message = Measurement(1) }).ShouldBeTrue();
        source.Post(record).ShouldBeTrue();
        source.Complete();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        selected.Count.ShouldBe(1);
        (await selected.ReceiveAsync(TimeSpan.FromSeconds(5))).Message.ToFlowEvent().Measurements["value"].ShouldBe(10);
    }

    private static FlowMessage Measurement(double value) => new FlowEvent
    {
        Name = "order.measured", Measurements = new Dictionary<string, double> { ["value"] = value }
    }.ToMessage();

    private static ApplicationReportCatalog Catalog(params ApplicationReportDescriptor[] descriptors)
        => new(descriptors.Select(descriptor => new TestReport(descriptor)));

    private sealed class TestReport(ApplicationReportDescriptor descriptor) : IApplicationReport
    {
        public ApplicationReportDescriptor Descriptor => descriptor;
        public bool Includes(ApplicationReportEvent record) => record.Message.ToFlowEvent().Name == descriptor.Type;
    }

    public sealed class CustomOrderReport : IApplicationReport
    {
        private readonly CompiledApplicationReportFilter _filter = ReportingFilterTests.Filter(
            "/measurements/value", ApplicationReportComparison.GreaterThan, "5").Compile();
        public ApplicationReportDescriptor Descriptor { get; } = new()
        { Type = "custom.orders", Category = "business", Description = "Custom order totals" };
        public bool Includes(ApplicationReportEvent record) => _filter.IsMatch(record);
    }

    public sealed class CustomAuditReport : IApplicationReport
    {
        public ApplicationReportDescriptor Descriptor { get; } = new()
        { Type = "custom.audit", Category = "audit", Description = "Custom audit records" };
        public bool Includes(ApplicationReportEvent record) => record.Message.ToFlowEvent().Name == "audit.recorded";
    }

    private static ApplicationReportFieldDescriptor Counter(string? resetScope) => new()
    {
        Path = "/measurements/count", Description = "Message count", Unit = "messages",
        Kind = ApplicationReportFieldKind.Number, Measurement = ApplicationReportMeasurementKind.Cumulative, ResetScope = resetScope
    };
}
