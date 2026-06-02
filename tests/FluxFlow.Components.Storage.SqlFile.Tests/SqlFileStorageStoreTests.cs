using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Storage.SqlFile.Tests;

public sealed class SqlFileStorageStoreTests
{
    [Fact]
    public async Task Put_PersistsRecordAcrossStoreInstances()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        var store = CreateStore(path);

        var saved = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first",
            ContentType = "text/plain",
            Attributes = new Dictionary<string, string>
            {
                ["source"] = "test"
            },
            CorrelationId = "c-1"
        });

        var reopened = CreateStore(path);
        var loaded = await reopened.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        saved.Version.ShouldBe(1);
        loaded.ShouldNotBeNull();
        loaded.Value.ShouldBe("first");
        loaded.ContentType.ShouldBe("text/plain");
        loaded.Attributes["source"].ShouldBe("test");
        loaded.CorrelationId.ShouldBe("c-1");
    }

    [Fact]
    public async Task Put_HonorsWriteModesAndExpectedVersion()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(Path.Combine(temp.Path, "records.db"));

        var created = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "one",
            Mode = StorageWriteMode.Create
        });
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "duplicate",
            Mode = StorageWriteMode.Create
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "missing",
            Value = "missing",
            Mode = StorageWriteMode.Replace
        }));
        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "bad-version",
            ExpectedVersion = created.Version + 1
        }));

        var replaced = await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "two",
            Mode = StorageWriteMode.Replace,
            ExpectedVersion = created.Version
        });

        replaced.Version.ShouldBe(2);
        replaced.Value.ShouldBe("two");
    }

    [Fact]
    public async Task Get_HonorsExpiration()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "expired",
            Value = "old",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var current = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "expired"
        });
        var expired = await store.GetAsync(new StorageGetRequest
        {
            Collection = "items",
            Key = "expired",
            IncludeExpired = true
        });

        current.ShouldBeNull();
        expired.ShouldNotBeNull();
        expired.Value.ShouldBe("old");
    }

    [Fact]
    public async Task Query_FiltersRecordsAndHonorsLimit()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-1",
            Value = "one",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "a-2",
            Value = "two",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "b-1",
            Value = "three",
            Attributes = new Dictionary<string, string> { ["kind"] = "beta" }
        });
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "other",
            Key = "a-3",
            Value = "other",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" }
        });

        var records = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            KeyPrefix = "a-",
            Attributes = new Dictionary<string, string> { ["kind"] = "alpha" },
            Limit = 1
        });

        records.ShouldHaveSingleItem().Key.ShouldBe("a-1");
    }

    [Fact]
    public async Task Query_HonorsExpiration()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "expired",
            Value = "old",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var current = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items"
        });
        var expired = await store.QueryAsync(new StorageQueryRequest
        {
            Collection = "items",
            IncludeExpired = true
        });

        current.ShouldBeEmpty();
        expired.ShouldHaveSingleItem().Key.ShouldBe("expired");
    }

    [Fact]
    public async Task Delete_ReturnsFoundAndMissingResults()
    {
        using var temp = TempDirectory.Create();
        var store = CreateStore(Path.Combine(temp.Path, "records.db"));
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });

        var deleted = await store.DeleteAsync(new StorageDeleteRequest
        {
            Collection = "items",
            Key = "alpha",
            CorrelationId = "delete-alpha"
        });
        var missing = await store.DeleteAsync(new StorageDeleteRequest
        {
            Collection = "items",
            Key = "alpha"
        });

        deleted.Found.ShouldBeTrue();
        deleted.Deleted.ShouldBeTrue();
        deleted.Record.ShouldNotBeNull();
        deleted.CorrelationId.ShouldBe("delete-alpha");
        missing.Found.ShouldBeFalse();
        missing.Deleted.ShouldBeFalse();
        missing.Record.ShouldBeNull();
    }

    [Fact]
    public async Task Put_RejectsValuesAboveConfiguredLimit()
    {
        using var temp = TempDirectory.Create();
        var store = new SqlFileStorageStore(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db"),
            MaxValueBytes = 5
        });

        await Should.ThrowAsync<InvalidOperationException>(() => store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "large",
            Value = "abcdef"
        }));
    }

    [Fact]
    public async Task Factory_UsesContextDefaultsAndCreatesOwnedLease()
    {
        using var temp = TempDirectory.Create();
        var factory = new SqlFileStorageStoreFactory(new SqlFileStorageStoreOptions
        {
            DatabasePath = Path.Combine(temp.Path, "records.db")
        });

        await using var lease = await factory.OpenAsync(new StorageStoreContext
        {
            Address = new NodeAddress("main", new NodeName("store")),
            NodeType = new NodeType("storage.put"),
            StoreName = "tenant-a",
            Collection = "items"
        });
        var saved = await lease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });

        lease.OwnsStore.ShouldBeTrue();
        saved.Collection.ShouldBe("items");
    }

    [Fact]
    public void Options_ValidateDatabasePath()
    {
        Should.Throw<InvalidOperationException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions()));

        using var temp = TempDirectory.Create();
        var missing = Path.Combine(temp.Path, "missing", "records.db");
        Should.Throw<DirectoryNotFoundException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions
            {
                DatabasePath = missing,
                CreateDirectory = false
            }));

        Should.Throw<FileNotFoundException>(
            () => new SqlFileStorageStore(new SqlFileStorageStoreOptions
            {
                DatabasePath = Path.Combine(temp.Path, "missing.db"),
                CreateDatabase = false
            }));
    }

    [Fact]
    public async Task Registration_StoresThroughStorageNode()
    {
        using var temp = TempDirectory.Create();
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSqlFileStorage(
                Path.Combine(temp.Path, "records.db")));
        registry.TryGetFactory(StorageComponentTypes.Put, out var factory).ShouldBeTrue();
        var runtimeNode = factory(CreateContext(StorageComponentTypes.Put, new
        {
            collection = "items",
            boundedCapacity = 4
        }));
        var input = runtimeNode.FindInput(new PortName(StorageComponentPorts.Input))
            .ShouldBeOfType<InputPort<StoragePutRequest>>();
        var output = LinkOutput<StorageResult>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "first"
        });
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Collection.ShouldBe("items");
        result.Key.ShouldBe("alpha");
        result.Version.ShouldBe(1);
        File.Exists(Path.Combine(temp.Path, "records.db")).ShouldBeTrue();
    }

    [Fact]
    public async Task Registration_QueriesThroughStorageNode()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "records.db");
        var store = CreateStore(path);
        await store.PutAsync(new StoragePutRequest
        {
            Collection = "items",
            Key = "alpha",
            Value = "first"
        });
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStorageComponents(options => options.UseSqlFileStorage(path));
        registry.TryGetFactory(StorageComponentTypes.Query, out var factory).ShouldBeTrue();
        var runtimeNode = factory(CreateContext(StorageComponentTypes.Query, new
        {
            collection = "items",
            boundedCapacity = 4
        }));
        var input = runtimeNode.FindInput(new PortName(StorageComponentPorts.Input))
            .ShouldBeOfType<InputPort<StorageQueryRequest>>();
        var output = LinkOutput<StorageQueryResult>(runtimeNode);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync(new StorageQueryRequest());
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Count.ShouldBe(1);
        result.Records.ShouldHaveSingleItem().Key.ShouldBe("alpha");
    }

    private static SqlFileStorageStore CreateStore(string databasePath)
        => new(new SqlFileStorageStoreOptions
        {
            DatabasePath = databasePath
        });

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType nodeType,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("storage"),
            new NodeDefinition
            {
                Type = nodeType,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = StorageComponentPorts.Result)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"fluxflow-sqlfile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
