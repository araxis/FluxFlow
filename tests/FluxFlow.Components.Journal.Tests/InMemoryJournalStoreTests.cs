using FluxFlow.Components.Journal.Contracts;
using FluxFlow.Components.Journal.Options;
using FluxFlow.Components.Journal.Stores;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Journal.Tests;

public sealed class InMemoryJournalStoreTests
{
    [Fact]
    public async Task JournalComponentOptions_requires_explicit_store_registration()
    {
        var options = new JournalComponentOptions();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            options.StoreFactory.OpenAsync(new JournalStoreContext()).AsTask());

        exception.Message.ShouldContain("Register one through JournalComponentOptions");
    }

    [Fact]
    public async Task UseStore_rejects_null_context_before_invoking_delegate()
    {
        var invoked = false;
        var options = new JournalComponentOptions()
            .UseStore(
                (_, _) =>
                {
                    invoked = true;
                    return ValueTask.FromResult(JournalStoreLease.Shared(new InMemoryJournalStore()));
                });

        await Should.ThrowAsync<ArgumentNullException>(() =>
            options.StoreFactory.OpenAsync(null!).AsTask());

        invoked.ShouldBeFalse();
    }

    [Fact]
    public async Task UseStore_rejects_null_lease_from_delegate()
    {
        var options = new JournalComponentOptions()
            .UseStore((_, _) => ValueTask.FromResult<JournalStoreLease>(null!));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            options.StoreFactory.OpenAsync(new JournalStoreContext()).AsTask());

        exception.Message.ShouldContain("null lease");
    }

    [Fact]
    public async Task UseSharedStore_rejects_null_store_from_delegate()
    {
        var options = new JournalComponentOptions()
            .UseSharedStore(_ => null!);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            options.StoreFactory.OpenAsync(new JournalStoreContext()).AsTask());

        exception.Message.ShouldContain("returned null");
    }

    [Fact]
    public async Task UseStore_receives_normalized_context_values()
    {
        var clock = new FixedTimeProvider(Timestamp(12));
        JournalStoreContext? observed = null;
        CancellationToken observedToken = default;
        var store = new InMemoryJournalStore();
        var options = new JournalComponentOptions()
            .UseStore(
                (context, cancellationToken) =>
                {
                    observed = context;
                    observedToken = cancellationToken;
                    return ValueTask.FromResult(JournalStoreLease.Shared(store));
                });

        using var cts = new CancellationTokenSource();
        await using var lease = await options.StoreFactory.OpenAsync(
            new JournalStoreContext
            {
                StoreName = " named ",
                Clock = clock
            },
            cts.Token);

        lease.Store.ShouldBeSameAs(store);
        lease.OwnsStore.ShouldBeFalse();
        observed.ShouldNotBeNull();
        observed.StoreName.ShouldBe("named");
        observed.Clock.ShouldBeSameAs(clock);
        observedToken.ShouldBe(cts.Token);
    }

    [Fact]
    public void JournalStoreContext_normalizes_blank_store_name_and_null_clock()
    {
        var context = new JournalStoreContext
        {
            StoreName = " ",
            Clock = null!
        };

        context.StoreName.ShouldBeNull();
        context.Clock.ShouldBeSameAs(TimeProvider.System);
    }

    [Fact]
    public async Task JournalStoreLease_disposes_only_owned_stores_once()
    {
        var ownedStore = new DisposableJournalStore();
        var owned = JournalStoreLease.Owned(ownedStore);
        var sharedStore = new DisposableJournalStore();
        var shared = JournalStoreLease.Shared(sharedStore);

        await owned.DisposeAsync();
        await owned.DisposeAsync();
        await shared.DisposeAsync();

        ownedStore.DisposeCount.ShouldBe(1);
        sharedStore.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Service_registration_registers_keyed_store_and_factory()
    {
        var store = new InMemoryJournalStore();
        var factory = new InMemoryJournalStoreFactory();
        var services = new ServiceCollection()
            .AddFluxFlowJournalStore("journal", store)
            .AddFluxFlowJournalStoreFactory("journal-factory", factory);

        await using var provider = services.BuildServiceProvider();
        var resolvedStore = provider.GetRequiredKeyedService<IJournalStore>("journal");
        var resolvedFactory = provider.GetRequiredKeyedService<IJournalStoreFactory>("journal-factory");
        await using var lease = await resolvedFactory.OpenAsync(new JournalStoreContext
        {
            StoreName = "journal"
        });

        resolvedStore.ShouldBeSameAs(store);
        resolvedFactory.ShouldBeSameAs(factory);
        lease.Store.ShouldNotBeNull();
        lease.OwnsStore.ShouldBeFalse();
    }

    [Fact]
    public async Task Service_registration_passes_provider_to_store_and_factory_providers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JournalRegistrationDependency("primary"));
        services
            .AddFluxFlowJournalStore(
                "journal",
                provider => new NamedJournalStore(
                    provider.GetRequiredService<JournalRegistrationDependency>().Name))
            .AddFluxFlowJournalStoreFactory(
                "journal-factory",
                provider => new NamedJournalStoreFactory(
                    provider.GetRequiredService<JournalRegistrationDependency>().Name));

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredKeyedService<IJournalStore>("journal")
            .ShouldBeOfType<NamedJournalStore>();
        var factory = provider.GetRequiredKeyedService<IJournalStoreFactory>("journal-factory")
            .ShouldBeOfType<NamedJournalStoreFactory>();

        store.Name.ShouldBe("primary");
        factory.Name.ShouldBe("primary");
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var store = new InMemoryJournalStore();
        var factory = new InMemoryJournalStoreFactory();

        Should.Throw<ArgumentNullException>(() =>
            JournalStoreServiceCollectionExtensions.AddFluxFlowJournalStore(
                null!,
                "journal",
                store))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            JournalStoreServiceCollectionExtensions.AddFluxFlowJournalStore(
                null!,
                "journal",
                (IJournalStore)null!))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowJournalStore(" ", store))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowJournalStore(" ", (IJournalStore)null!))
            .ParamName.ShouldBe("name");
        var storeException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowJournalStore(
                "journal",
                (IJournalStore)null!));
        var storeFactoryDelegateException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowJournalStore(
                "journal",
                (Func<IServiceProvider, IJournalStore>)null!));

        Should.Throw<ArgumentNullException>(() =>
            JournalStoreServiceCollectionExtensions.AddFluxFlowJournalStoreFactory(
                null!,
                "journal-factory",
                factory))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            JournalStoreServiceCollectionExtensions.AddFluxFlowJournalStoreFactory(
                null!,
                "journal-factory",
                (IJournalStoreFactory)null!))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowJournalStoreFactory(" ", factory))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowJournalStoreFactory(" ", (IJournalStoreFactory)null!))
            .ParamName.ShouldBe("name");
        var factoryException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowJournalStoreFactory(
                "journal-factory",
                (IJournalStoreFactory)null!));
        var factoryProviderException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowJournalStoreFactory(
                "journal-factory",
                (Func<IServiceProvider, IJournalStoreFactory>)null!));

        storeException.ParamName.ShouldBe("store");
        storeFactoryDelegateException.ParamName.ShouldBe("storeFactory");
        factoryException.ParamName.ShouldBe("storeFactory");
        factoryProviderException.ParamName.ShouldBe("storeFactory");
    }

    [Fact]
    public async Task Service_registration_rejects_null_provider_results()
    {
        var services = new ServiceCollection()
            .AddFluxFlowJournalStore(
                "journal",
                static _ => null!)
            .AddFluxFlowJournalStoreFactory(
                "journal-factory",
                static _ => null!);

        await using var provider = services.BuildServiceProvider();

        var storeException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IJournalStore>("journal"));
        var factoryException = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IJournalStoreFactory>("journal-factory"));

        storeException.Message.ShouldBe("Journal store provider returned null.");
        factoryException.Message.ShouldBe("Journal store factory provider returned null.");
    }

    [Fact]
    public async Task InMemoryJournalStoreFactory_rejects_null_context_before_opening()
    {
        var factory = new InMemoryJournalStoreFactory();

        await Should.ThrowAsync<ArgumentNullException>(() =>
            factory.OpenAsync(null!).AsTask());
    }

    [Fact]
    public async Task InMemoryJournalStoreFactory_shares_named_stores_and_separates_other_names()
    {
        var factory = new InMemoryJournalStoreFactory();

        await using var alpha1 = await factory.OpenAsync(new JournalStoreContext
        {
            StoreName = " alpha "
        });
        await using var alpha2 = await factory.OpenAsync(new JournalStoreContext
        {
            StoreName = "alpha"
        });
        await using var beta = await factory.OpenAsync(new JournalStoreContext
        {
            StoreName = "beta"
        });

        alpha1.OwnsStore.ShouldBeFalse();
        alpha2.OwnsStore.ShouldBeFalse();
        beta.OwnsStore.ShouldBeFalse();
        alpha1.Store.ShouldBeSameAs(alpha2.Store);
        alpha1.Store.ShouldNotBeSameAs(beta.Store);

        await alpha1.Store.AppendAsync(CreateRecord("alpha-1"));

        var alphaResult = await alpha2.Store.QueryAsync(new JournalQuery());
        var betaResult = await beta.Store.QueryAsync(new JournalQuery());

        alphaResult.Records.Select(record => record.Id).ShouldBe(["alpha-1"]);
        betaResult.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task InMemoryJournalStoreFactory_applies_retention_to_created_stores()
    {
        var factory = new InMemoryJournalStoreFactory(new JournalRetentionOptions
        {
            MaxRecords = 1
        });
        await using var lease = await factory.OpenAsync(new JournalStoreContext
        {
            StoreName = "retained"
        });

        await lease.Store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await lease.Store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));

        var result = await lease.Store.QueryAsync(new JournalQuery());
        result.Records.Select(record => record.Id).ShouldBe(["2"]);
    }

    [Fact]
    public async Task QueryAsync_filters_records_by_event_fields_and_attributes()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord(
            "1",
            type: "order.created",
            status: "ok",
            source: "worker",
            workflowId: "wf-1",
            workflowName: "orders",
            nodeId: "node-1",
            componentId: "component-1",
            subject: "orders/42",
            channel: "events/orders",
            severity: "info",
            level: "low",
            attributes: new Dictionary<string, string> { ["tenant"] = "primary" }));
        await store.AppendAsync(CreateRecord(
            "2",
            type: "order.failed",
            status: "failed",
            source: "worker",
            workflowId: "wf-1",
            workflowName: "orders",
            nodeId: "node-2",
            componentId: "component-2",
            subject: "orders/42",
            channel: "events/orders",
            severity: "error",
            level: "high",
            attributes: new Dictionary<string, string> { ["tenant"] = "secondary" }));

        var result = await store.QueryAsync(new JournalQuery
        {
            TypePrefix = "order.",
            Status = "ok",
            Source = "worker",
            WorkflowId = "wf-1",
            WorkflowName = "orders",
            NodeId = "node-1",
            ComponentId = "component-1",
            SubjectPrefix = "orders/",
            ChannelPrefix = "events/",
            Severity = "info",
            Level = "low",
            Attributes = new Dictionary<string, string> { ["tenant"] = "primary" }
        });

        result.TotalMatched.ShouldBe(1);
        result.Records.Single().Id.ShouldBe("1");
        result.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task QueryAsync_excludes_subject_and_channel_prefixes()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", subject: "jobs/private/1", channel: "internal/jobs"));
        await store.AppendAsync(CreateRecord("2", subject: "jobs/public/2", channel: "public/jobs"));

        var result = await store.QueryAsync(new JournalQuery
        {
            SubjectPrefix = "jobs/",
            ChannelPrefix = "public/",
            ExcludedSubjectPrefix = "jobs/private/",
            ExcludedChannelPrefix = "internal/"
        });

        result.Records.Select(record => record.Id).ShouldBe(["2"]);
    }

    [Fact]
    public async Task QueryAsync_applies_time_range_and_pagination()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        await store.AppendAsync(CreateRecord("4", timestamp: Timestamp(3)));

        var result = await store.QueryAsync(new JournalQuery
        {
            From = Timestamp(1),
            To = Timestamp(3),
            Offset = 1,
            Limit = 1
        });

        result.TotalMatched.ShouldBe(3);
        result.Records.Select(record => record.Id).ShouldBe(["3"]);
        result.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task PruneAsync_removes_old_records_and_keeps_latest_max_records()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        await store.AppendAsync(CreateRecord("4", timestamp: Timestamp(3)));

        var prune = await store.PruneAsync(new JournalRetentionOptions
        {
            DeleteBefore = Timestamp(1),
            MaxRecords = 2
        });
        var result = await store.QueryAsync(new JournalQuery());

        prune.Removed.ShouldBe(2);
        prune.Remaining.ShouldBe(2);
        result.Records.Select(record => record.Id).ShouldBe(["3", "4"]);
    }

    [Fact]
    public async Task PruneAsync_uses_reference_time_for_age_based_retention()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("old", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("new", timestamp: Timestamp(5)));

        await store.PruneAsync(new JournalRetentionOptions
        {
            MaxAge = TimeSpan.FromMinutes(2),
            ReferenceTime = Timestamp(6)
        });
        var result = await store.QueryAsync(new JournalQuery());

        result.Records.Select(record => record.Id).ShouldBe(["new"]);
    }

    [Fact]
    public async Task AppendAsync_applies_max_records_retention_on_append()
    {
        var store = new InMemoryJournalStore(new JournalRetentionOptions
        {
            MaxRecords = 2
        });

        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));
        await store.AppendAsync(CreateRecord("3", timestamp: Timestamp(2)));
        var result = await store.QueryAsync(new JournalQuery());

        result.Records.Select(record => record.Id).ShouldBe(["2", "3"]);
    }

    [Fact]
    public void Constructor_rejects_negative_max_records_retention()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new InMemoryJournalStore(new JournalRetentionOptions
            {
                MaxRecords = -1
            }));
    }

    [Fact]
    public void RetentionOptions_reject_invalid_numeric_values()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new JournalRetentionOptions
        {
            MaxRecords = -1
        });
        Should.Throw<ArgumentOutOfRangeException>(() => new JournalRetentionOptions
        {
            MaxAge = TimeSpan.Zero
        });
        Should.Throw<ArgumentOutOfRangeException>(() => new JournalRetentionOptions
        {
            MaxAge = TimeSpan.FromMilliseconds(-1)
        });
    }

    [Fact]
    public async Task AppendAsync_detects_duplicates_after_retention_and_prune()
    {
        var store = new InMemoryJournalStore(new JournalRetentionOptions
        {
            MaxRecords = 2
        });
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(0)));
        await store.AppendAsync(CreateRecord("2", timestamp: Timestamp(1)));

        await store.PruneAsync(new JournalRetentionOptions { MaxRecords = 1 });

        // "1" was pruned, so its id can be appended again; "2" still exists.
        await store.AppendAsync(CreateRecord("1", timestamp: Timestamp(2)));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.AppendAsync(CreateRecord("2", timestamp: Timestamp(3))).AsTask());

        var result = await store.QueryAsync(new JournalQuery());
        result.Records.Select(record => record.Id).ShouldBe(["2", "1"]);
    }

    [Fact]
    public async Task AppendAsync_rejects_duplicate_record_ids()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord("same"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.AppendAsync(CreateRecord("same")).AsTask());
    }

    [Fact]
    public async Task AppendAsync_normalizes_record_fields_and_attributes()
    {
        var store = new InMemoryJournalStore();

        var appended = await store.AppendAsync(CreateRecord(
            "  spaced-id  ",
            type: " item.accepted ",
            status: " ok ",
            source: " source-a ",
            workflowId: " wf-1 ",
            workflowName: " main ",
            nodeId: " node-1 ",
            componentId: " component-1 ",
            subject: " items/1 ",
            channel: " events/items ",
            severity: " info ",
            level: " low ",
            attributes: new Dictionary<string, string>
            {
                [" tenant "] = " primary "
            }));

        appended.Record.Id.ShouldBe("spaced-id");
        appended.Record.Type.ShouldBe("item.accepted");
        appended.Record.Status.ShouldBe("ok");
        appended.Record.Source.ShouldBe("source-a");
        appended.Record.WorkflowId.ShouldBe("wf-1");
        appended.Record.WorkflowName.ShouldBe("main");
        appended.Record.NodeId.ShouldBe("node-1");
        appended.Record.ComponentId.ShouldBe("component-1");
        appended.Record.Subject.ShouldBe("items/1");
        appended.Record.Channel.ShouldBe("events/items");
        appended.Record.Severity.ShouldBe("info");
        appended.Record.Level.ShouldBe("low");
        appended.Record.Attributes.ContainsKey("tenant").ShouldBeTrue();
        appended.Record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void JournalRecord_normalizes_fields_and_copies_attributes()
    {
        var attributes = new Dictionary<string, string>
        {
            [" tenant "] = " primary "
        };

        var record = CreateRecord(
            "  id  ",
            type: " type ",
            status: " ok ",
            source: " source ",
            workflowId: " wf-1 ",
            workflowName: " main ",
            nodeId: " node-1 ",
            componentId: " component-1 ",
            subject: " subject ",
            channel: " channel ",
            severity: " info ",
            level: " low ",
            attributes: attributes) with
        {
            Summary = " summary ",
            PayloadPreview = " preview "
        };

        attributes["tenant"] = "changed";

        record.Id.ShouldBe("id");
        record.Type.ShouldBe("type");
        record.Status.ShouldBe("ok");
        record.Source.ShouldBe("source");
        record.WorkflowId.ShouldBe("wf-1");
        record.WorkflowName.ShouldBe("main");
        record.NodeId.ShouldBe("node-1");
        record.ComponentId.ShouldBe("component-1");
        record.Subject.ShouldBe("subject");
        record.Channel.ShouldBe("channel");
        record.Severity.ShouldBe("info");
        record.Level.ShouldBe("low");
        record.Summary.ShouldBe("summary");
        record.PayloadPreview.ShouldBe("preview");
        record.Attributes["tenant"].ShouldBe("primary");
        record.Attributes.ContainsKey(" tenant ").ShouldBeFalse();
    }

    [Fact]
    public void JournalRecord_rejects_blank_attribute_values()
    {
        Should.Throw<ArgumentException>(() =>
            CreateRecord(
                "blank-attribute",
                attributes: new Dictionary<string, string>
                {
                    ["tenant"] = " "
                }));
    }

    [Fact]
    public void JournalRecord_rejects_duplicate_attribute_keys_after_trimming()
    {
        Should.Throw<ArgumentException>(() =>
            CreateRecord(
                "duplicate-attributes",
                attributes: new Dictionary<string, string>
                {
                    [" tenant "] = "primary",
                    ["tenant"] = "secondary"
                }));
    }

    [Fact]
    public async Task QueryAsync_uses_normalized_query_fields_and_attributes()
    {
        var store = new InMemoryJournalStore();
        await store.AppendAsync(CreateRecord(
            "1",
            type: "order.created",
            status: "ok",
            source: "worker",
            subject: "orders/42",
            channel: "events/orders",
            severity: "info",
            level: "low",
            attributes: new Dictionary<string, string> { ["tenant"] = "primary" }));

        var attributes = new Dictionary<string, string>
        {
            [" tenant "] = " primary "
        };
        var query = new JournalQuery
        {
            TypePrefix = " order. ",
            Status = " ok ",
            Source = " worker ",
            SubjectPrefix = " orders/ ",
            ChannelPrefix = " events/ ",
            Severity = " info ",
            Level = " low ",
            Attributes = attributes
        };

        attributes["tenant"] = "changed";
        var result = await store.QueryAsync(query);

        query.TypePrefix.ShouldBe("order.");
        query.Attributes["tenant"].ShouldBe("primary");
        result.Records.Select(record => record.Id).ShouldBe(["1"]);
    }

    [Fact]
    public void QueryResult_copies_records()
    {
        var records = new List<JournalRecord>
        {
            CreateRecord("1")
        };

        var result = new JournalQueryResult
        {
            Records = records,
            TotalMatched = 1,
            Offset = 0,
            HasMore = false
        };

        records.Clear();

        result.Records.Count.ShouldBe(1);
        result.Records[0].Id.ShouldBe("1");
    }

    [Fact]
    public void JournalQueryMatcher_validates_query_shape()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            JournalQueryMatcher.Validate(new JournalQuery { Offset = -1 }));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            JournalQueryMatcher.Validate(new JournalQuery { Limit = 0 }));
        Should.Throw<ArgumentException>(() =>
            JournalQueryMatcher.Validate(new JournalQuery
            {
                From = Timestamp(2),
                To = Timestamp(1)
            }));
    }

    [Fact]
    public async Task QueryAsync_validates_query_shape()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            store.QueryAsync(new JournalQuery { Offset = -1 }).AsTask());
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            store.QueryAsync(new JournalQuery { Limit = 0 }).AsTask());
        await Should.ThrowAsync<ArgumentException>(() =>
            store.QueryAsync(new JournalQuery
            {
                From = Timestamp(2),
                To = Timestamp(1)
            }).AsTask());
    }

    [Fact]
    public async Task PruneAsync_requires_reference_time_for_max_age()
    {
        var store = new InMemoryJournalStore();

        await Should.ThrowAsync<ArgumentException>(() =>
            store.PruneAsync(new JournalRetentionOptions
            {
                MaxAge = TimeSpan.FromMinutes(1)
            }).AsTask());
    }

    [Fact]
    public void FromEvent_maps_neutral_event_fields()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = "item.accepted",
            Source = "source-a",
            SourceNodeId = "node-1",
            Subject = "items/1",
            Status = "ok",
            Channel = "items",
            PayloadBytes = 12,
            PayloadPreview = "preview",
            Attributes = new Dictionary<string, string>
            {
                [JournalRecordMapper.WorkflowIdAttribute] = "wf-1",
                [JournalRecordMapper.WorkflowNameAttribute] = "main",
                [JournalRecordMapper.ComponentIdAttribute] = "component-1",
                [JournalRecordMapper.SeverityAttribute] = "info",
                [JournalRecordMapper.LevelAttribute] = "low",
                [JournalRecordMapper.SummaryAttribute] = "accepted"
            }
        };

        var record = JournalRecordMapper.FromEvent(input, "evt-1");

        record.Id.ShouldBe("evt-1");
        record.Timestamp.ShouldBe(input.Timestamp);
        record.Type.ShouldBe(input.Type);
        record.Source.ShouldBe(input.Source);
        record.NodeId.ShouldBe("node-1");
        record.ComponentId.ShouldBe("component-1");
        record.WorkflowId.ShouldBe("wf-1");
        record.WorkflowName.ShouldBe("main");
        record.Severity.ShouldBe("info");
        record.Level.ShouldBe("low");
        record.Summary.ShouldBe("accepted");
        record.PayloadBytes.ShouldBe(12);
        record.PayloadPreview.ShouldBe("preview");
    }

    [Fact]
    public void FromEvent_uses_node_attribute_when_source_node_id_is_missing()
    {
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = "item.accepted",
            Attributes = new Dictionary<string, string>
            {
                [JournalRecordMapper.NodeIdAttribute] = "node-from-attributes"
            }
        };

        var record = JournalRecordMapper.FromEvent(input, "evt-1");

        record.NodeId.ShouldBe("node-from-attributes");
    }

    [Fact]
    public void FromEvent_normalizes_event_fields_and_attributes()
    {
        var attributes = new Dictionary<string, string>
        {
            [" tenant "] = " primary ",
            [JournalRecordMapper.WorkflowIdAttribute] = " wf-1 ",
            [JournalRecordMapper.SummaryAttribute] = " summary "
        };
        var input = new JournalEventInput
        {
            Timestamp = Timestamp(1),
            Type = " item.accepted ",
            Source = " source-a ",
            SourceNodeId = " node-1 ",
            Subject = " items/1 ",
            Status = " ok ",
            Channel = " events/items ",
            PayloadPreview = " accepted ",
            Attributes = attributes
        };

        attributes["tenant"] = "changed";
        var record = JournalRecordMapper.FromEvent(input, " evt-1 ");

        input.Type.ShouldBe("item.accepted");
        input.Attributes["tenant"].ShouldBe("primary");
        record.Id.ShouldBe("evt-1");
        record.Type.ShouldBe("item.accepted");
        record.Source.ShouldBe("source-a");
        record.NodeId.ShouldBe("node-1");
        record.Subject.ShouldBe("items/1");
        record.Status.ShouldBe("ok");
        record.Channel.ShouldBe("events/items");
        record.WorkflowId.ShouldBe("wf-1");
        record.Summary.ShouldBe("summary");
        record.PayloadPreview.ShouldBe("accepted");
        record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void FromEvent_rejects_duplicate_attribute_keys_after_trimming()
    {
        Should.Throw<ArgumentException>(() =>
            new JournalEventInput
            {
                Timestamp = Timestamp(1),
                Attributes = new Dictionary<string, string>
                {
                    [" tenant "] = "primary",
                    ["tenant"] = "secondary"
                }
            });
    }

    [Fact]
    public void FromEvent_rejects_blank_record_id()
    {
        Should.Throw<ArgumentException>(() =>
            JournalRecordMapper.FromEvent(new JournalEventInput { Timestamp = Timestamp(1) }, " "));
    }

    private static JournalRecord CreateRecord(
        string id,
        DateTimeOffset? timestamp = null,
        string type = "item.accepted",
        string status = "ok",
        string source = "source-a",
        string? workflowId = null,
        string? workflowName = null,
        string? nodeId = null,
        string? componentId = null,
        string? subject = null,
        string? channel = null,
        string? severity = null,
        string? level = null,
        Dictionary<string, string>? attributes = null)
        => new()
        {
            Id = id,
            Timestamp = timestamp ?? Timestamp(0),
            Type = type,
            Status = status,
            Source = source,
            WorkflowId = workflowId,
            WorkflowName = workflowName,
            NodeId = nodeId,
            ComponentId = componentId,
            Subject = subject,
            Channel = channel,
            Severity = severity,
            Level = level,
            Attributes = attributes ?? []
        };

    private static DateTimeOffset Timestamp(int minute)
        => new(2026, 1, 1, 0, minute, 0, TimeSpan.Zero);

    private sealed class DisposableJournalStore : IJournalStore, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask<JournalAppendResult> AppendAsync(
            JournalRecord record,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JournalQueryResult> QueryAsync(
            JournalQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JournalPruneResult> PruneAsync(
            JournalRetentionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => now;
    }

    private sealed record JournalRegistrationDependency(string Name);

    private sealed class NamedJournalStore(string name) : IJournalStore
    {
        public string Name { get; } = name;

        public ValueTask<JournalAppendResult> AppendAsync(
            JournalRecord record,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JournalQueryResult> QueryAsync(
            JournalQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JournalPruneResult> PruneAsync(
            JournalRetentionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NamedJournalStoreFactory(string name) : IJournalStoreFactory
    {
        public string Name { get; } = name;

        public ValueTask<JournalStoreLease> OpenAsync(
            JournalStoreContext context,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
