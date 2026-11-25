using Bookstore.Domain;
using Bookstore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Bookstore.Tests;

public sealed class AccountPersistenceTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("bookstore-accounts-").FullName;
    private string DatabasePath => Path.Combine(directory, "test.db");

    private SqliteCustomerAccounts Start()
    {
        var database = new SqliteDatabase(DatabasePath);
        database.Initialize();
        return new(database);
    }

    [Fact]
    public void SignupDeduplicatesEmailAndRecordsConfirmation()
    {
        var accounts = Start();

        Assert.True(accounts.Create("first", "name@example.test", "Tên", "hash", AccountRoles.Customer, true));
        Assert.False(accounts.Create("second", "NAME@example.test", "Khác", "hash", AccountRoles.Customer, true));
        Assert.Equal("first", accounts.Credentials("NAME@example.test")?.Account.Username);
        Assert.Equal("Xác nhận đăng ký", Assert.Single(accounts.Mail("first")).Subject);
        Assert.Empty(accounts.Mail("second"));
    }

    [Fact]
    public void LoginThrottlesFiveFailuresAndResetsAfterTimeout()
    {
        var accounts = Start();
        accounts.Create("first", "name@example.test", "", "hash", AccountRoles.Customer, false);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        for (var i = 0; i < 5; i++)
            accounts.RecordFailedAttempt("first", now);

        Assert.Equal(now.AddMinutes(5), accounts.Credentials("first")?.LockedUntil);
        Assert.False(accounts.StartSession("first", "hash", "blocked", now, now.AddHours(8)));

        now = now.AddMinutes(5);
        Assert.True(accounts.StartSession("first", "hash", "first-session", now, now.AddHours(8)));
        Assert.Null(accounts.Credentials("first")?.LockedUntil);
        Assert.Equal(now, accounts.Find("first")?.LastLogin);
    }

    [Fact]
    public void LockAndRoleChangeRevokeExistingSessions()
    {
        var accounts = Start();
        accounts.Create("first", "name@example.test", "", "hash", AccountRoles.Customer, false);
        var now = DateTimeOffset.UtcNow;

        Assert.True(accounts.StartSession("first", "hash", "one", now, now.AddHours(8)));
        Assert.True(accounts.UpdateAccess("first", AccountRoles.Customer, locked: true));
        Assert.Null(accounts.SessionAccount("first", "one", now));
        Assert.False(accounts.StartSession("first", "hash", "two", now, now.AddHours(8)));

        Assert.True(accounts.UpdateAccess("first", AccountRoles.Customer, locked: false));
        Assert.True(accounts.StartSession("first", "hash", "three", now, now.AddHours(8)));
        Assert.True(accounts.UpdateAccess("first", AccountRoles.Sales, locked: false));
        Assert.Null(accounts.SessionAccount("first", "three", now));
        Assert.Equal(AccountRoles.Sales, accounts.Find("first")?.Role);
    }

    [Fact]
    public void AddressDefaultsAreScopedToOwnerAndPromotedAfterDeletion()
    {
        var accounts = Start();
        accounts.Create("first", "first@example.test", "", "hash", AccountRoles.Customer, false);
        accounts.Create("second", "second@example.test", "", "hash", AccountRoles.Customer, false);
        var address = new ShippingAddress(0, "Người nhận", "0912345678", "HCM", "Quận 5", "Phường 1", "12 A", false);

        Assert.True(accounts.SaveAddress("first", address));
        var first = Assert.Single(accounts.Addresses("first"));
        Assert.True(first.IsDefault);
        Assert.False(accounts.SaveAddress("second", first with { IsDefault = true }));
        Assert.Null(accounts.Address("second", first.Id));
        Assert.False(accounts.DeleteAddress("second", first.Id));
        Assert.True(accounts.SaveAddress("first", address with { Street = "22 A", IsDefault = true }));

        var chosen = Assert.Single(accounts.Addresses("first"), row => row.IsDefault);
        Assert.Equal("22 A", chosen.Street);
        Assert.True(accounts.DeleteAddress("first", chosen.Id));
        Assert.True(Assert.Single(accounts.Addresses("first")).IsDefault);
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        }.ToString());
        SqliteConnection.ClearPool(connection);
        Directory.Delete(directory, recursive: true);
    }
}

