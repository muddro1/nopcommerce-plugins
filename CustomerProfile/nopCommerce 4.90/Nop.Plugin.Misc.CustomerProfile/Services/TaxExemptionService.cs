using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

public interface ITaxExemptionService
{
    Task<TaxExemptionCertificate?> GetByIdAsync(int id);

    /// <summary>Newest first</summary>
    Task<IList<TaxExemptionCertificate>> GetForCustomerAsync(int customerId);

    /// <summary>The approved certificate valid today with the latest expiry, if any</summary>
    Task<TaxExemptionCertificate?> GetCurrentValidAsync(int customerId);

    /// <summary>Approved and pending certificates: the only states the daily task acts on</summary>
    Task<IList<TaxExemptionCertificate>> GetForPlanningAsync();

    Task<IPagedList<CertificateRow>> SearchAsync(CertificateFilter filter, string? dealership, string? customer,
        int pageIndex, int pageSize);
}

public class TaxExemptionService : ITaxExemptionService
{
    private readonly IRepository<TaxExemptionCertificate> _certificateRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CustomerBusinessProfile> _profileRepository;
    private readonly IStoreClock _clock;
    private readonly CustomerProfileSettings _settings;

    public TaxExemptionService(IRepository<TaxExemptionCertificate> certificateRepository,
        IRepository<Customer> customerRepository,
        IRepository<CustomerBusinessProfile> profileRepository,
        IStoreClock clock,
        CustomerProfileSettings settings)
    {
        _certificateRepository = certificateRepository;
        _customerRepository = customerRepository;
        _profileRepository = profileRepository;
        _clock = clock;
        _settings = settings;
    }

    public async Task<TaxExemptionCertificate?> GetByIdAsync(int id) =>
        id <= 0 ? null : await _certificateRepository.GetByIdAsync(id);

    public async Task<IList<TaxExemptionCertificate>> GetForCustomerAsync(int customerId) =>
        await _certificateRepository.Table
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.UploadedOnUtc).ThenByDescending(c => c.Id)
            .ToListAsync();

    public async Task<TaxExemptionCertificate?> GetCurrentValidAsync(int customerId)
    {
        var today = _clock.Today;
        return await _certificateRepository.Table
            .Where(c => c.CustomerId == customerId && c.StateId == (int)CertificateState.Approved && c.ExpiresOn >= today)
            .OrderByDescending(c => c.ExpiresOn)
            .FirstOrDefaultAsync();
    }

    public async Task<IList<TaxExemptionCertificate>> GetForPlanningAsync() =>
        await _certificateRepository.Table
            .Where(c => c.StateId == (int)CertificateState.Approved || c.StateId == (int)CertificateState.PendingReview)
            .ToListAsync();

    public async Task<IPagedList<CertificateRow>> SearchAsync(CertificateFilter filter, string? dealership, string? customer,
        int pageIndex, int pageSize)
    {
        var today = _clock.Today;
        var reminderEnd = today.AddDays(Math.Max(_settings.ReminderDaysBeforeExpiry, 0));

        var certificates = _certificateRepository.Table;
        certificates = filter switch
        {
            CertificateFilter.PendingReview => certificates.Where(c => c.StateId == (int)CertificateState.PendingReview),
            CertificateFilter.Approved => certificates.Where(c => c.StateId == (int)CertificateState.Approved),
            CertificateFilter.Expiring => certificates.Where(c => c.StateId == (int)CertificateState.Approved
                                                                  && c.ExpiresOn >= today && c.ExpiresOn <= reminderEnd),
            CertificateFilter.Expired => certificates.Where(c => c.StateId == (int)CertificateState.Expired),
            CertificateFilter.Rejected => certificates.Where(c => c.StateId == (int)CertificateState.Rejected),
            _ => certificates
        };

        var query =
            from c in certificates
            join cu in _customerRepository.Table on c.CustomerId equals cu.Id
            join p in _profileRepository.Table on c.CustomerId equals p.CustomerId into profiles
            from p in profiles.DefaultIfEmpty()
            select new { c, cu, Dealership = p == null ? null : p.BmwDealership };

        if (!string.IsNullOrWhiteSpace(dealership))
        {
            var term = dealership.Trim();
            query = query.Where(x => x.Dealership != null && x.Dealership.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(customer))
        {
            var term = customer.Trim();
            query = query.Where(x => x.cu.Email.Contains(term) || x.cu.FirstName.Contains(term)
                                     || x.cu.LastName.Contains(term) || x.cu.Company.Contains(term));
        }

        // Work queue order: pending oldest first, approved soonest to expire first, everything else newest first
        query = filter switch
        {
            CertificateFilter.PendingReview => query.OrderBy(x => x.c.UploadedOnUtc),
            CertificateFilter.Approved or CertificateFilter.Expiring => query.OrderBy(x => x.c.ExpiresOn),
            _ => query.OrderByDescending(x => x.c.UploadedOnUtc)
        };

        var page = await query.ToPagedListAsync(pageIndex, pageSize);
        var rows = page.Select(x => new CertificateRow(x.c, x.cu.Email, $"{x.cu.FirstName} {x.cu.LastName}".Trim(),
            x.cu.Company, x.Dealership, x.cu.IsTaxExempt)).ToList();

        return new PagedList<CertificateRow>(rows, page.PageIndex, page.PageSize, page.TotalCount);
    }
}
