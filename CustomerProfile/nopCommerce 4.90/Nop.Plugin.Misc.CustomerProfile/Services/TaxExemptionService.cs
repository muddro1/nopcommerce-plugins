using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;

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

    /// <summary>
    /// Stores an uploaded certificate. Customer uploads wait for review; admin uploads are approved at once
    /// and need an expiry date.
    /// </summary>
    /// <returns>Errors to show; empty on success</returns>
    Task<IList<string>> UploadAsync(int customerId, IFormFile? file, DateTime? expiresOn, string? certificateNumber,
        string? issuingState, int uploadedByCustomerId, bool approveImmediately);

    /// <summary>Approves a pending certificate, supersedes the customer's previous one, and marks them tax exempt if set to</summary>
    Task<IList<string>> ApproveAsync(int certificateId, DateTime? expiresOn, int reviewerCustomerId, string? note);

    Task<IList<string>> RejectAsync(int certificateId, int reviewerCustomerId, string? note);

    /// <summary>Daily task: marks expired, optionally removes exemption, sends the expired emails</summary>
    Task ExpireAsync(int certificateId, bool removeExemption);

    Task SendReminderAsync(int certificateId);
    Task SendPendingNudgeAsync(int certificateId);

    /// <summary>The certificate and its file, or nulls when either is missing</summary>
    Task<(TaxExemptionCertificate? Certificate, Download? Download)> GetFileAsync(int certificateId);
}

public class TaxExemptionService : ITaxExemptionService
{
    private readonly IRepository<TaxExemptionCertificate> _certificateRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CustomerBusinessProfile> _profileRepository;
    private readonly IStoreClock _clock;
    private readonly CustomerProfileSettings _settings;
    private readonly ICustomerProfileMessageService _messageService;
    private readonly ICustomerService _customerService;
    private readonly IDownloadService _downloadService;
    private readonly ILocalizationService _localizationService;

    public TaxExemptionService(IRepository<TaxExemptionCertificate> certificateRepository,
        IRepository<Customer> customerRepository,
        IRepository<CustomerBusinessProfile> profileRepository,
        IStoreClock clock,
        CustomerProfileSettings settings,
        ICustomerProfileMessageService messageService,
        ICustomerService customerService,
        IDownloadService downloadService,
        ILocalizationService localizationService)
    {
        _certificateRepository = certificateRepository;
        _customerRepository = customerRepository;
        _profileRepository = profileRepository;
        _clock = clock;
        _settings = settings;
        _messageService = messageService;
        _customerService = customerService;
        _downloadService = downloadService;
        _localizationService = localizationService;
    }

    private Task<string> TAsync(string key) => _localizationService.GetResourceAsync(CustomerProfileDefaults.LocalePrefix + key);

    private static string? Clean(string? value, int maxLength)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > maxLength ? value[..maxLength] : value;
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

    #region Changes

    public async Task<IList<string>> UploadAsync(int customerId, IFormFile? file, DateTime? expiresOn, string? certificateNumber,
        string? issuingState, int uploadedByCustomerId, bool approveImmediately)
    {
        if (file == null || file.Length == 0)
            return new List<string> { await TAsync("TaxExemption.Error.Empty") };

        if (file.Length > _settings.MaxUploadSizeMb * 1024L * 1024L)
            return new List<string> { string.Format(await TAsync("TaxExemption.Error.TooLarge"), _settings.MaxUploadSizeMb) };

        if (approveImmediately)
        {
            var dateErrors = await ValidateExpiryAsync(expiresOn);
            if (dateErrors.Any())
                return dateErrors;
        }

        var bytes = await _downloadService.GetDownloadBitsAsync(file);
        var (problem, contentType) = UploadValidator.Validate(file.FileName, bytes, _settings.MaxUploadSizeMb);
        switch (problem)
        {
            case UploadProblem.Empty:
                return new List<string> { await TAsync("TaxExemption.Error.Empty") };
            case UploadProblem.TooLarge:
                return new List<string> { string.Format(await TAsync("TaxExemption.Error.TooLarge"), _settings.MaxUploadSizeMb) };
            case UploadProblem.UnsupportedType:
                return new List<string> { await TAsync("TaxExemption.Error.UnsupportedType") };
        }

        var fileName = Path.GetFileName(file.FileName);
        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadUrl = string.Empty,
            DownloadBinary = bytes,
            ContentType = contentType,
            Filename = Path.GetFileNameWithoutExtension(fileName),
            Extension = Path.GetExtension(fileName).ToLowerInvariant(),
            IsNew = true
        };
        await _downloadService.InsertDownloadAsync(download);

        var certificate = new TaxExemptionCertificate
        {
            CustomerId = customerId,
            DownloadId = download.Id,
            FileName = Clean(fileName, 400) ?? "certificate",
            ContentType = contentType!,
            State = CertificateState.PendingReview,
            ExpiresOn = expiresOn?.Date,
            CertificateNumber = Clean(certificateNumber, 100),
            IssuingState = Clean(issuingState, 100),
            UploadedByCustomerId = uploadedByCustomerId,
            UploadedOnUtc = _clock.UtcNow
        };
        await _certificateRepository.InsertAsync(certificate);

        if (approveImmediately)
            return await ApproveAsync(certificate.Id, expiresOn, uploadedByCustomerId, null);

        await _messageService.SendCertificateSubmittedAsync(certificate);
        return new List<string>();
    }

    private async Task<IList<string>> ValidateExpiryAsync(DateTime? expiresOn)
    {
        if (!expiresOn.HasValue)
            return new List<string> { await TAsync("TaxExemption.Error.ExpiryRequired") };
        if (expiresOn.Value.Date < _clock.Today)
            return new List<string> { await TAsync("TaxExemption.Error.ExpiryInPast") };
        return new List<string>();
    }

    public async Task<IList<string>> ApproveAsync(int certificateId, DateTime? expiresOn, int reviewerCustomerId, string? note)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate == null)
            return new List<string> { await TAsync("NotFound") };
        if (certificate.State != CertificateState.PendingReview)
            return new List<string> { await TAsync("TaxExemption.Error.NotPending") };

        var errors = await ValidateExpiryAsync(expiresOn);
        if (errors.Any())
            return errors;

        // One active certificate per customer: the previous approved one is kept as history
        var previous = await _certificateRepository.Table
            .Where(c => c.CustomerId == certificate.CustomerId && c.Id != certificate.Id && c.StateId == (int)CertificateState.Approved)
            .ToListAsync();
        foreach (var old in previous)
        {
            old.State = CertificateState.Superseded;
            await _certificateRepository.UpdateAsync(old);
        }

        certificate.State = CertificateState.Approved;
        certificate.ExpiresOn = expiresOn!.Value.Date;
        certificate.ReviewedByCustomerId = reviewerCustomerId;
        certificate.ReviewedOnUtc = _clock.UtcNow;
        certificate.ReviewNote = Clean(note, CustomerProfileDefaults.NoteMaxLength);
        certificate.ReminderSentOnUtc = null;
        certificate.ExpiredNoticeSentOnUtc = null;
        await _certificateRepository.UpdateAsync(certificate);

        if (_settings.ApproveMarksTaxExempt)
        {
            var customer = await _customerService.GetCustomerByIdAsync(certificate.CustomerId);
            if (customer is { IsTaxExempt: false })
            {
                customer.IsTaxExempt = true;
                await _customerService.UpdateCustomerAsync(customer);
            }
        }

        await _messageService.SendCertificateReviewedAsync(certificate);
        return errors;
    }

    public async Task<IList<string>> RejectAsync(int certificateId, int reviewerCustomerId, string? note)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate == null)
            return new List<string> { await TAsync("NotFound") };
        if (certificate.State != CertificateState.PendingReview)
            return new List<string> { await TAsync("TaxExemption.Error.NotPending") };

        var cleanNote = Clean(note, CustomerProfileDefaults.NoteMaxLength);
        if (cleanNote == null)
            return new List<string> { await TAsync("TaxExemption.Error.NoteRequired") };

        certificate.State = CertificateState.Rejected;
        certificate.ReviewedByCustomerId = reviewerCustomerId;
        certificate.ReviewedOnUtc = _clock.UtcNow;
        certificate.ReviewNote = cleanNote;
        await _certificateRepository.UpdateAsync(certificate);

        await _messageService.SendCertificateReviewedAsync(certificate);
        return new List<string>();
    }

    public async Task ExpireAsync(int certificateId, bool removeExemption)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate is not { State: CertificateState.Approved })
            return;

        certificate.State = CertificateState.Expired;
        await _certificateRepository.UpdateAsync(certificate);

        var customer = await _customerService.GetCustomerByIdAsync(certificate.CustomerId);
        var removed = false;
        if (removeExemption && customer is { IsTaxExempt: true })
        {
            customer.IsTaxExempt = false;
            var note = string.Format(await TAsync("TaxExemption.AdminComment"), _clock.Today.ToShortDateString(), certificate.Id,
                certificate.ExpiresOn?.ToShortDateString());
            customer.AdminComment = string.IsNullOrWhiteSpace(customer.AdminComment) ? note : customer.AdminComment + Environment.NewLine + note;
            await _customerService.UpdateCustomerAsync(customer);
            removed = true;
        }

        // Send, then stamp: a failure in between re-sends once rather than never
        await _messageService.SendExpiredAsync(certificate, removed);
        certificate.ExpiredNoticeSentOnUtc = _clock.UtcNow;
        await _certificateRepository.UpdateAsync(certificate);
    }

    public async Task SendReminderAsync(int certificateId)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate is not { State: CertificateState.Approved, ReminderSentOnUtc: null })
            return;

        await _messageService.SendExpiringAsync(certificate);
        certificate.ReminderSentOnUtc = _clock.UtcNow;
        await _certificateRepository.UpdateAsync(certificate);
    }

    public async Task SendPendingNudgeAsync(int certificateId)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate is not { State: CertificateState.PendingReview, PendingNudgeSentOnUtc: null })
            return;

        await _messageService.SendPendingNudgeAsync(certificate);
        certificate.PendingNudgeSentOnUtc = _clock.UtcNow;
        await _certificateRepository.UpdateAsync(certificate);
    }

    public async Task<(TaxExemptionCertificate? Certificate, Download? Download)> GetFileAsync(int certificateId)
    {
        var certificate = await GetByIdAsync(certificateId);
        if (certificate == null)
            return (null, null);

        var download = await _downloadService.GetDownloadByIdAsync(certificate.DownloadId);
        return download?.DownloadBinary == null ? (null, null) : (certificate, download);
    }

    #endregion
}
