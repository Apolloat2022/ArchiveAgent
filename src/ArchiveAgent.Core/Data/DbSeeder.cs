using ArchiveAgent.Core.Domain;

namespace ArchiveAgent.Core.Data;

/// <summary>Seeds a sample dataset so you can run the pipeline immediately.</summary>
public static class DbSeeder
{
    public static void Seed(ArchiveDbContext db)
    {
        if (db.Records.Any()) return;

        var rnd = new Random(42);
        var types = new[] { "Invoice", "PartRecord", "Log", "CustomerNote", "Transaction" };
        var now = DateTime.UtcNow;

        var records = new List<Record>();
        for (var i = 1; i <= 200; i++)
        {
            var type = types[rnd.Next(types.Length)];
            records.Add(new Record
            {
                ExternalId = $"EXT-{i:00000}",
                Type = type,
                Title = $"{type} #{i}",
                Content = SampleContent(type, rnd),
                CreatedUtc = now.AddDays(-rnd.Next(30, 3650)), // 1 month to ~10 years old
                Status = RecordStatus.Pending
            });
        }

        db.Records.AddRange(records);
        db.SaveChanges();
    }

    private static string SampleContent(string type, Random rnd) => type switch
    {
        "Invoice"      => $"Invoice total ${rnd.Next(50, 9000)}, customer account on file.",
        "PartRecord"   => $"OEM part number AB{rnd.Next(1000, 9999)}, supersession data, no personal info.",
        "Log"          => "System event log entry, routine operation, no user data.",
        "CustomerNote" => "Customer contact: name and phone number on file regarding a service request.",
        "Transaction"  => $"Payment transaction, card ending {rnd.Next(1000, 9999)}.",
        _              => "Generic record content."
    };
}
