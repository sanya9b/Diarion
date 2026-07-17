using System;
using System.IO;
using System.Linq;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class EncryptedLiteDatabaseFactoryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public EncryptedLiteDatabaseFactoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_enc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "test.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static bool CanOpenAndRead(string path, string? password)
    {
        try
        {
            var cs = string.IsNullOrEmpty(password)
                ? new ConnectionString { Filename = path }
                : new ConnectionString { Filename = path, Password = password };

            using var db = new LiteDatabase(cs);
            _ = db.GetCollection<Item>("items").FindAll().ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void Open_FreshPath_CreatesEncryptedDatabaseUnreadableWithoutKey()
    {
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "key-123"))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 1, Name = "alpha" });
        }

        CanOpenAndRead(_path, null).Should().BeFalse();      // without the key -> unreadable
        CanOpenAndRead(_path, "key-123").Should().BeTrue();  // with the key -> readable
    }

    [Fact]
    public void Open_LegacyUnencryptedFile_MigratesInPlaceAndPreservesData()
    {
        // Arrange: a pre-existing UNENCRYPTED database with data (pre-encryption build).
        using (var plain = new LiteDatabase(new ConnectionString { Filename = _path }))
        {
            plain.GetCollection<Item>("items").Insert(new Item { Id = 7, Name = "legacy" });
        }
        CanOpenAndRead(_path, null).Should().BeTrue(); // sanity: currently plaintext

        // Act: open through the factory with a key -> transparent migration.
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "new-key"))
        {
            var items = db.GetCollection<Item>("items").FindAll().ToList();
            items.Should().ContainSingle(i => i.Id == 7 && i.Name == "legacy");
        }

        // Assert: now encrypted, data intact, and no leftover backup.
        CanOpenAndRead(_path, null).Should().BeFalse();
        CanOpenAndRead(_path, "new-key").Should().BeTrue();
        File.Exists(_path + ".premigration.bak").Should().BeFalse();
    }

    [Fact]
    public void Open_ExistingEncryptedFileWithWrongKey_FailsAndLeavesOriginalIntact()
    {
        // Arrange: encrypted DB created with the correct key.
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "correct-key"))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 3, Name = "secret" });
        }

        // Act: attempt to open with the WRONG key (simulates a lost/rotated key).
        Action act = () => EncryptedLiteDatabaseFactory.Open(_path, "wrong-key").Dispose();

        // Assert: fail-closed (throws) without corrupting data; the original key still works.
        act.Should().Throw<Exception>();
        CanOpenAndRead(_path, "correct-key").Should().BeTrue();
    }

    [Fact]
    public void Open_EmptyPassword_OpensUnencrypted()
    {
        // Used only when no key provider is configured (e.g. certain tests); must not encrypt.
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, null))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 5, Name = "plain" });
        }

        CanOpenAndRead(_path, null).Should().BeTrue();
    }

    [Fact]
    public void IsValidEncryptedDatabase_ValidDbWithRequiredCollection_ReturnsTrue()
    {
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "key"))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 1, Name = "x" });
        }

        EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(_path, "key", "items").Should().BeTrue();
    }

    [Fact]
    public void IsValidEncryptedDatabase_WrongKey_ReturnsFalse()
    {
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "correct"))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 1, Name = "x" });
        }

        EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(_path, "wrong", "items").Should().BeFalse();
    }

    [Fact]
    public void IsValidEncryptedDatabase_MissingRequiredCollection_ReturnsFalse()
    {
        using (var db = EncryptedLiteDatabaseFactory.Open(_path, "key"))
        {
            db.GetCollection<Item>("items").Insert(new Item { Id = 1, Name = "x" });
        }

        EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(_path, "key", "entries").Should().BeFalse();
    }

    [Fact]
    public void IsValidEncryptedDatabase_ForeignOrCorruptFile_ReturnsFalse()
    {
        File.WriteAllBytes(_path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(_path, "key", "entries").Should().BeFalse();
    }

    [Fact]
    public void IsValidEncryptedDatabase_NonexistentFile_ReturnsFalse()
    {
        var missing = Path.Combine(_dir, "does-not-exist.db");

        EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(missing, "key", "entries").Should().BeFalse();
    }
}
