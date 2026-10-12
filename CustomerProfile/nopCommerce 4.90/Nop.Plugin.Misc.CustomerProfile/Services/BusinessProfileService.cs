using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

public interface IBusinessProfileService
{
    Task<CustomerBusinessProfile?> GetAsync(int customerId);

    /// <summary>The saved profile, or a new unsaved one with default Net 30 terms</summary>
    Task<CustomerBusinessProfile> GetOrNewAsync(int customerId);

    Task<IList<CustomerBusinessProfile>> GetByCustomerIdsAsync(IList<int> customerIds);

    /// <summary>Inserts or updates. Trims the dealership and clamps terms to 1-365 days.</summary>
    Task SaveAsync(CustomerBusinessProfile profile);
}

public class BusinessProfileService : IBusinessProfileService
{
    private readonly IRepository<CustomerBusinessProfile> _repository;
    private readonly CustomerProfileSettings _settings;

    public BusinessProfileService(IRepository<CustomerBusinessProfile> repository, CustomerProfileSettings settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public async Task<CustomerBusinessProfile?> GetAsync(int customerId)
    {
        if (customerId <= 0)
            return null;

        return await _repository.Table.FirstOrDefaultAsync(p => p.CustomerId == customerId);
    }

    public async Task<CustomerBusinessProfile> GetOrNewAsync(int customerId) =>
        await GetAsync(customerId) ?? new CustomerBusinessProfile
        {
            CustomerId = customerId,
            Net30TermsDays = _settings.Net30DefaultTermsDays
        };

    public async Task<IList<CustomerBusinessProfile>> GetByCustomerIdsAsync(IList<int> customerIds)
    {
        if (customerIds.Count == 0)
            return new List<CustomerBusinessProfile>();

        return await _repository.Table.Where(p => customerIds.Contains(p.CustomerId)).ToListAsync();
    }

    public async Task SaveAsync(CustomerBusinessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var dealership = profile.BmwDealership?.Trim();
        profile.BmwDealership = string.IsNullOrEmpty(dealership) ? null
            : dealership.Length > CustomerProfileDefaults.DealershipMaxLength ? dealership[..CustomerProfileDefaults.DealershipMaxLength]
            : dealership;
        profile.Net30TermsDays = Math.Clamp(profile.Net30TermsDays, 1, 365);
        profile.UpdatedOnUtc = DateTime.UtcNow;

        if (profile.Id == 0)
            await _repository.InsertAsync(profile);
        else
            await _repository.UpdateAsync(profile);
    }
}
