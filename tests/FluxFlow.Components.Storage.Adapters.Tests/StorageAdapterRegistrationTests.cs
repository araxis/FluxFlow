using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.FileSystem;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Components.Storage.SqlFile;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Adapters.Tests;

public sealed class StorageAdapterRegistrationTests
{
    [Fact]
    public async Task FileSystemAndSqlFileBackendsCanBeConfiguredSideBySide()
    {
        using var workspace = TempWorkspace.Create();
        var fileStoreOptions = new StorageComponentOptions()
            .UseFileSystemStorage(new FileSystemStorageStoreOptions
            {
                RootDirectory = workspace.CreateDirectory("files"),
                DefaultCollection = "records"
            });
        var sqlStoreOptions = new StorageComponentOptions()
            .UseSqlFileStorage(new SqlFileStorageStoreOptions
            {
                DatabasePath = workspace.CreateFilePath("sql", "records.db"),
                DefaultCollection = "records"
            });

        await using var fileLease = await fileStoreOptions.StoreFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary"
        });
        await using var sqlLease = await sqlStoreOptions.StoreFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary"
        });

        fileLease.OwnsStore.ShouldBeFalse();
        sqlLease.OwnsStore.ShouldBeTrue();

        await fileLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "same-key",
            Value = "file-backed"
        });
        await sqlLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "same-key",
            Value = "sql-backed"
        });

        var fileRecord = await fileLease.Store.GetAsync(new StorageGetRequest
        {
            Key = "same-key"
        });
        var sqlRecord = await sqlLease.Store.GetAsync(new StorageGetRequest
        {
            Key = "same-key"
        });

        fileRecord.ShouldNotBeNull();
        sqlRecord.ShouldNotBeNull();
        fileRecord.Value.ShouldBe("file-backed");
        sqlRecord.Value.ShouldBe("sql-backed");
        fileRecord.Collection.ShouldBe("records");
        sqlRecord.Collection.ShouldBe("records");
        fileRecord.Version.ShouldBe(1);
        sqlRecord.Version.ShouldBe(1);
    }

    [Fact]
    public async Task BackendFactoriesCanBeRegisteredAsKeyedResources()
    {
        using var workspace = TempWorkspace.Create();
        var services = new ServiceCollection();
        services.AddFluxFlowFileSystemStorageStoreFactory(
            "files",
            new FileSystemStorageStoreOptions
            {
                RootDirectory = workspace.CreateDirectory("files"),
                DefaultCollection = "records"
            });
        services.AddFluxFlowSqlFileStorageStoreFactory(
            "database",
            new SqlFileStorageStoreOptions
            {
                DatabasePath = workspace.CreateFilePath("sql", "records.db"),
                DefaultCollection = "records"
            });

        await using var provider = services.BuildServiceProvider();
        var fileFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("files");
        var databaseFactory = provider.GetRequiredKeyedService<IStorageStoreFactory>("database");

        await using var fileLease = await fileFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary"
        });
        await using var databaseLease = await databaseFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary"
        });

        fileLease.OwnsStore.ShouldBeFalse();
        databaseLease.OwnsStore.ShouldBeTrue();

        var fileRecord = await fileLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "file"
        });
        var databaseRecord = await databaseLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "database"
        });

        fileRecord.Collection.ShouldBe("records");
        databaseRecord.Collection.ShouldBe("records");
        fileRecord.Value.ShouldBe("file");
        databaseRecord.Value.ShouldBe("database");
    }

    [Fact]
    public async Task StringRegistrationHelpersConfigureBackendFactories()
    {
        using var workspace = TempWorkspace.Create();
        var fileOptions = new StorageComponentOptions()
            .UseFileSystemStorage(workspace.CreateDirectory("files"));
        var databaseOptions = new StorageComponentOptions()
            .UseSqlFileStorage(workspace.CreateFilePath("sql", "records.db"));

        await using var fileLease = await fileOptions.StoreFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary",
            Collection = "records"
        });
        await using var databaseLease = await databaseOptions.StoreFactory.OpenAsync(new StorageStoreContext
        {
            StoreName = "primary",
            Collection = "records"
        });

        var fileRecord = await fileLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "file"
        });
        var databaseRecord = await databaseLease.Store.PutAsync(new StoragePutRequest
        {
            Key = "alpha",
            Value = "database"
        });

        fileLease.OwnsStore.ShouldBeFalse();
        databaseLease.OwnsStore.ShouldBeTrue();
        fileRecord.Collection.ShouldBe("records");
        fileRecord.Value.ShouldBe("file");
        databaseRecord.Collection.ShouldBe("records");
        databaseRecord.Value.ShouldBe("database");
    }

    [Fact]
    public void RegistrationHelpersRejectInvalidArguments()
    {
        var options = new StorageComponentOptions();
        var fileSystemOptions = new FileSystemStorageStoreOptions
        {
            RootDirectory = "data/files"
        };
        var sqlFileOptions = new SqlFileStorageStoreOptions
        {
            DatabasePath = "data/records.db"
        };

        Should.Throw<ArgumentNullException>(() =>
            FileSystemStorageRegistrationExtensions.UseFileSystemStorage(null!, fileSystemOptions))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            options.UseFileSystemStorage((string)null!))
            .ParamName.ShouldBe("rootDirectory");
        Should.Throw<ArgumentException>(() =>
            options.UseFileSystemStorage(" "))
            .ParamName.ShouldBe("rootDirectory");
        Should.Throw<ArgumentNullException>(() =>
            options.UseFileSystemStorage((FileSystemStorageStoreOptions)null!))
            .ParamName.ShouldBe("fileSystemOptions");

        Should.Throw<ArgumentNullException>(() =>
            SqlFileStorageRegistrationExtensions.UseSqlFileStorage(null!, sqlFileOptions))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            options.UseSqlFileStorage((string)null!))
            .ParamName.ShouldBe("databasePath");
        Should.Throw<ArgumentException>(() =>
            options.UseSqlFileStorage(" "))
            .ParamName.ShouldBe("databasePath");
        Should.Throw<ArgumentNullException>(() =>
            options.UseSqlFileStorage((SqlFileStorageStoreOptions)null!))
            .ParamName.ShouldBe("sqlFileOptions");
    }

    private sealed class TempWorkspace : IDisposable
    {
        private readonly string _root;

        private TempWorkspace(string root)
        {
            _root = root;
        }

        public static TempWorkspace Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "fluxflow-storage-adapters-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempWorkspace(root);
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateFilePath(params string[] segments)
        {
            var path = Path.Combine([_root, .. segments]);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
