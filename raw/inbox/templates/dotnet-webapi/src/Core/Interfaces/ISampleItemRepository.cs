using {{ProjectName}}.Core.Entities.Sample;

namespace {{ProjectName}}.Core.Interfaces;

public interface ISampleItemRepository : IRepository<SampleItem>
{
    Task<SampleItem?> GetByCodeAsync(string code);
}
