using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;

internal sealed class SmokeDatabase : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"ura-festa-db-{Guid.NewGuid():N}");

    internal SmokeDatabase()
    {
        Directory.CreateDirectory(directory);
        Write("events_male.br", Array.Empty<Story>());
        Write("events_female.br", Array.Empty<Story>());
        Write("skill_data.br", Array.Empty<SkillData>());
        Write("skill_upgrade_speciality.br", Array.Empty<SkillUpgradeSpeciality>());
        Write("talent_skill_sets.br", new Dictionary<int, TalentSkillData[]>());
        Write("factor_ids.br", new Dictionary<int, string>());
        Write("wins_saddle.br", Array.Empty<int>());
        Write("succession_relation.br", new SuccessionRelationTable());
        Write("names.br", Fixture.Names.ToList(), new()
        {
            TypeNameHandling = TypeNameHandling.All,
            ContractResolver = new WritablePropertiesContractResolver(),
        });
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory);
            if (Database.Initialize().GetAwaiter().GetResult() != DatabaseAvailability.Ready)
                throw new InvalidOperationException("Synthetic smoke database did not load.");
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    void Write<T>(string name, T value, JsonSerializerSettings? settings = null)
    {
        using var file = File.Create(Path.Combine(directory, name));
        using var brotli = new BrotliStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(brotli, new UTF8Encoding(false));
        writer.Write(JsonConvert.SerializeObject(value, settings));
    }

    sealed class WritablePropertiesContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization)
            => [.. base.CreateProperties(type, serialization).Where(property => property.Writable)];
    }
}
