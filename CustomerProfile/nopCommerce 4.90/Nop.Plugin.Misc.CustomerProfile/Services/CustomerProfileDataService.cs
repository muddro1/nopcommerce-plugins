using System.IO.Compression;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using Nop.Services.Media;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

/// <summary>Export of all plugin data as a zip, and removal of all plugin data</summary>
public interface ICustomerProfileDataService
{
    /// <summary>
    /// Writes a zip: certificates.csv plus the certificate files matching the filter, and business-profiles.csv,
    /// contacts.csv and net30-orders.csv with everything.
    /// </summary>
    Task WriteExportAsync(Stream output, CertificateFilter filter, string? dealership, string? customer);

    /// <summary>Deletes every row in the plugin's tables and the certificate files. Customers and orders are untouched.</summary>
    Task<DeletedCounts> DeleteAllAsync();
}

public record DeletedCounts(int Certificates, int Profiles, int Contacts, int Net30Orders);

public class CustomerProfileDataService : ICustomerProfileDataService
{
    private const int PageSize = 500;

    private readonly IDownloadService _downloadService;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CustomerBusinessProfile> _profileRepository;
    private readonly IRepository<CustomerContact> _contactRepository;
    private readonly IRepository<Net30Order> _net30Repository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<TaxExemptionCertificate> _certificateRepository;
    private readonly IStoreClock _clock;
    private readonly ITaxExemptionService _taxExemptionService;

    public CustomerProfileDataService(IDownloadService downloadService,
        IRepository<Customer> customerRepository,
        IRepository<CustomerBusinessProfile> profileRepository,
        IRepository<CustomerContact> contactRepository,
        IRepository<Net30Order> net30Repository,
        IRepository<Order> orderRepository,
        IRepository<TaxExemptionCertificate> certificateRepository,
        IStoreClock clock,
        ITaxExemptionService taxExemptionService)
    {
        _downloadService = downloadService;
        _customerRepository = customerRepository;
        _profileRepository = profileRepository;
        _contactRepository = contactRepository;
        _net30Repository = net30Repository;
        _orderRepository = orderRepository;
        _certificateRepository = certificateRepository;
        _clock = clock;
        _taxExemptionService = taxExemptionService;
    }

    #region Export

    public async Task WriteExportAsync(Stream output, CertificateFilter filter, string? dealership, string? customer)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteCertificatesAsync(zip, filter, dealership, customer);
        await WriteProfilesAsync(zip);
        await WriteContactsAsync(zip);
        await WriteNet30OrdersAsync(zip);
    }

    private static async Task WriteCsvAsync(ZipArchive zip, string name, string header, IEnumerable<string> lines)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), Csv.Encoding);
        await writer.WriteLineAsync(header);
        foreach (var line in lines)
            await writer.WriteLineAsync(line);
    }

    private async Task WriteCertificatesAsync(ZipArchive zip, CertificateFilter filter, string? dealership, string? customer)
    {
        var lines = new List<string>();

        for (var pageIndex = 0; ; pageIndex++)
        {
            var page = await _taxExemptionService.SearchAsync(filter, dealership, customer, pageIndex, PageSize);
            foreach (var row in page)
            {
                var cert = row.Certificate;
                var path = $"certificates/{cert.CustomerId}/{cert.Id}-{Csv.SafeFileName(cert.FileName)}";

                // The file goes in as-is: PDFs and images are already compressed
                var download = await _downloadService.GetDownloadByIdAsync(cert.DownloadId);
                if (download?.DownloadBinary is { Length: > 0 } bytes)
                {
                    await using var stream = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
                    await stream.WriteAsync(bytes);
                }
                else
                {
                    path = "(file missing)";
                }

                lines.Add(Csv.Line(cert.Id, cert.CustomerId, row.CustomerEmail, row.CustomerName, row.Company, row.Dealership,
                    row.IsTaxExempt, cert.State, cert.ExpiresOn, cert.CertificateNumber, cert.IssuingState,
                    _clock.ToStoreDate(cert.UploadedOnUtc), cert.UploadedByCustomerId == cert.CustomerId ? "Customer" : "Admin",
                    cert.ReviewedOnUtc.HasValue ? _clock.ToStoreDate(cert.ReviewedOnUtc.Value) : null, cert.ReviewNote,
                    cert.ExemptionRemovedOnExpiry, path));
            }

            if (!page.HasNextPage)
                break;
        }

        await WriteCsvAsync(zip, "certificates.csv",
            "CertificateId,CustomerId,CustomerEmail,CustomerName,Company,BmwDealership,CustomerIsTaxExempt,Status,ExpiresOn,"
            + "CertificateNumber,IssuingState,UploadedOn,UploadedBy,ReviewedOn,ReviewNote,ExemptionRemovedOnExpiry,File", lines);
    }

    private async Task WriteProfilesAsync(ZipArchive zip)
    {
        var rows = await (
            from p in _profileRepository.Table
            join c in _customerRepository.Table on p.CustomerId equals c.Id
            orderby c.Email
            select new { p, c.Email, c.FirstName, c.LastName, c.Company, c.IsTaxExempt }).ToListAsync();

        await WriteCsvAsync(zip, "business-profiles.csv",
            "CustomerId,CustomerEmail,CustomerName,Company,BmwDealership,Net30Enabled,Net30TermsDays,CustomerIsTaxExempt",
            rows.Select(r => Csv.Line(r.p.CustomerId, r.Email, $"{r.FirstName} {r.LastName}".Trim(), r.Company,
                r.p.BmwDealership, r.p.Net30Enabled, r.p.Net30TermsDays, r.IsTaxExempt)));
    }

    private async Task WriteContactsAsync(ZipArchive zip)
    {
        var rows = await (
            from k in _contactRepository.Table
            join c in _customerRepository.Table on k.CustomerId equals c.Id
            orderby c.Email, k.Name
            select new { k, c.Email }).ToListAsync();

        await WriteCsvAsync(zip, "contacts.csv",
            "ContactId,CustomerId,CustomerEmail,Name,Email,Title,Active,Categories",
            rows.Select(r => Csv.Line(r.k.Id, r.k.CustomerId, r.Email, r.k.Name, r.k.Email, r.k.Title, r.k.Active,
                string.Join("; ", MessageCategories.All.Where(c => r.k.Categories.HasFlag(c))))));
    }

    private async Task WriteNet30OrdersAsync(ZipArchive zip)
    {
        var rows = await (
            from n in _net30Repository.Table
            join o in _orderRepository.Table on n.OrderId equals o.Id
            join c in _customerRepository.Table on n.CustomerId equals c.Id
            orderby n.DueOn
            select new { n, o.CustomOrderNumber, o.OrderTotal, o.PaymentStatusId, o.OrderStatusId, o.Deleted, c.Email }).ToListAsync();

        await WriteCsvAsync(zip, "net30-orders.csv",
            "OrderId,OrderNumber,CustomerId,CustomerEmail,OrderTotal,TermsDays,DueOn,PaymentStatus,OrderStatus,OrderDeleted,OverdueReminderSent",
            rows.Select(r => Csv.Line(r.n.OrderId, r.CustomOrderNumber, r.n.CustomerId, r.Email, r.OrderTotal, r.n.TermsDays, r.n.DueOn,
                (Nop.Core.Domain.Payments.PaymentStatus)r.PaymentStatusId, (OrderStatus)r.OrderStatusId, r.Deleted,
                r.n.OverdueNoticeSentOnUtc.HasValue ? _clock.ToStoreDate(r.n.OverdueNoticeSentOnUtc.Value) : null)));
    }

    #endregion

    #region Delete

    public async Task<DeletedCounts> DeleteAllAsync()
    {
        // Files first: once the certificate rows are gone nothing points at them
        var downloadIds = await _certificateRepository.Table.Select(c => c.DownloadId).ToListAsync();
        foreach (var downloadId in downloadIds)
        {
            var download = await _downloadService.GetDownloadByIdAsync(downloadId);
            if (download != null)
                await _downloadService.DeleteDownloadAsync(download);
        }

        return new DeletedCounts(
            await _certificateRepository.DeleteAsync(_ => true),
            await _profileRepository.DeleteAsync(_ => true),
            await _contactRepository.DeleteAsync(_ => true),
            await _net30Repository.DeleteAsync(_ => true));
    }

    #endregion
}
