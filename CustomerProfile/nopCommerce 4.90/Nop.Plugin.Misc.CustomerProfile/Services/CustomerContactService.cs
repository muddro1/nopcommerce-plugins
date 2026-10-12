using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

public interface ICustomerContactService
{
    Task<IList<CustomerContact>> GetForCustomerAsync(int customerId, bool activeOnly = false);
    Task<CustomerContact?> GetByIdAsync(int id);
    Task<int> CountForCustomerAsync(int customerId);
    Task InsertAsync(CustomerContact contact);
    Task UpdateAsync(CustomerContact contact);
    Task DeleteAsync(CustomerContact contact);
}

public class CustomerContactService : ICustomerContactService
{
    private readonly IRepository<CustomerContact> _repository;

    public CustomerContactService(IRepository<CustomerContact> repository)
    {
        _repository = repository;
    }

    public async Task<IList<CustomerContact>> GetForCustomerAsync(int customerId, bool activeOnly = false)
    {
        if (customerId <= 0)
            return new List<CustomerContact>();

        var query = _repository.Table.Where(c => c.CustomerId == customerId);
        if (activeOnly)
            query = query.Where(c => c.Active);

        return await query.OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync();
    }

    public async Task<CustomerContact?> GetByIdAsync(int id) =>
        id <= 0 ? null : await _repository.GetByIdAsync(id);

    public async Task<int> CountForCustomerAsync(int customerId) =>
        await _repository.Table.CountAsync(c => c.CustomerId == customerId);

    public async Task InsertAsync(CustomerContact contact)
    {
        Normalize(contact);
        contact.CreatedOnUtc = DateTime.UtcNow;
        await _repository.InsertAsync(contact);
    }

    public async Task UpdateAsync(CustomerContact contact)
    {
        Normalize(contact);
        contact.UpdatedOnUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(contact);
    }

    public Task DeleteAsync(CustomerContact contact) => _repository.DeleteAsync(contact);

    private static void Normalize(CustomerContact contact)
    {
        contact.Name = contact.Name.Trim();
        contact.Email = contact.Email.Trim();
        contact.Title = string.IsNullOrWhiteSpace(contact.Title) ? null : contact.Title.Trim();
    }
}
