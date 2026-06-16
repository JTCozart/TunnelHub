using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

// Dev-only helper: insert an active API key for an existing user.
// Usage: SeedKey <db-path> <email> <raw-key>
var dbPath = args[0];
var email = args[1];
var raw = args[2];

var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

using var cn = new SqliteConnection($"Data Source={dbPath}");
cn.Open();

string? uid;
using (var q = cn.CreateCommand())
{
    q.CommandText = "SELECT Id FROM AspNetUsers WHERE Email = $e";
    q.Parameters.AddWithValue("$e", email);
    uid = q.ExecuteScalar() as string;
}
if (uid is null) { Console.Error.WriteLine($"No user {email}"); return 1; }

using (var del = cn.CreateCommand())
{
    del.CommandText = "DELETE FROM ApiKeys WHERE Label = 'e2e'";
    del.ExecuteNonQuery();
}

using (var ins = cn.CreateCommand())
{
    ins.CommandText = @"INSERT INTO ApiKeys (Id,OwnerId,Label,DisplayPrefix,KeyHash,CreatedAt,IsActive)
                        VALUES ($id,$o,'e2e','th_e2e…',$h,$t,1)";
    // EF Core stores GUIDs as UPPERCASE text in SQLite; match that so the Tunnel FK resolves.
    ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
    ins.Parameters.AddWithValue("$o", uid);
    ins.Parameters.AddWithValue("$h", hash);
    ins.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("o"));
    ins.ExecuteNonQuery();
}
Console.WriteLine($"Seeded key for {email} (user {uid})");
return 0;
