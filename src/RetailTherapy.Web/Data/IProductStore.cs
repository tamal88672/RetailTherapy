using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

public interface IProductStore
{
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct);
    Task UpsertAsync(Product product, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
    /// <summary>Writes the seed products only when the store has none.</summary>
    Task SeedIfEmptyAsync(IReadOnlyList<Product> seed, CancellationToken ct);
}
