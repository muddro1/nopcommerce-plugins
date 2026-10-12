using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;

namespace Nop.Plugin.Misc.CustomerProfile.Controllers;

// The two FMS Plugins → Customer Profile lists
public partial class CustomerProfileAdminController
{
    private const int ListPageSize = 50;

    [HttpGet]
    public async Task<IActionResult> Net30Orders(bool overdueOnly = false, int page = 1)
    {
        if (!await CanViewOrdersAsync())
            return AccessDeniedView();

        var rows = await _net30Service.SearchUnpaidAsync(overdueOnly, Math.Max(page, 1) - 1, ListPageSize);
        return View(CustomerProfileDefaults.ViewsPath + "/Admin/Net30Orders.cshtml", new Net30OrdersModel
        {
            OverdueOnly = overdueOnly,
            Today = _clock.Today,
            Rows = rows,
            PageIndex = rows.PageIndex,
            TotalPages = rows.TotalPages,
            TotalCount = rows.TotalCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> TaxExemptions(CertificateFilter filter = CertificateFilter.PendingReview,
        string? dealership = null, string? customer = null, int page = 1)
    {
        if (!await CanViewCustomersAsync())
            return AccessDeniedView();

        var rows = await _taxExemptionService.SearchAsync(filter, dealership, customer, Math.Max(page, 1) - 1, ListPageSize);
        return View(CustomerProfileDefaults.ViewsPath + "/Admin/TaxExemptions.cshtml", new TaxExemptionsModel
        {
            Filter = filter,
            Dealership = dealership,
            Customer = customer,
            Rows = rows,
            PageIndex = rows.PageIndex,
            TotalPages = rows.TotalPages,
            TotalCount = rows.TotalCount
        });
    }
}
