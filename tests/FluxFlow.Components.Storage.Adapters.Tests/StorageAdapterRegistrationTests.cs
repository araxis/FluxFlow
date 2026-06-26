using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.FileSystem;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Components.Storage.SqlFile;
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
