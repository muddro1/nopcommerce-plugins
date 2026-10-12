using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Models;

namespace Nop.Plugin.Misc.CustomerProfile.Controllers;

// AJAX endpoints behind the Business profile card on the customer edit page
public partial class CustomerProfileAdminController
{
    private const string ContactListView = CustomerProfileDefaults.ViewsPath + "/Admin/_ContactList.cshtml";
    private const string CertificateListView = CustomerProfileDefaults.ViewsPath + "/Admin/_CertificateList.cshtml";

    private JsonResult Failure(params string[] errors) => Json(new { success = false, errors });

    #region Contacts

    [HttpGet]
    public async Task<IActionResult> ContactList(int customerId)
    {
        if (!await CanViewCustomersAsync())
            return Forbid();

        return PartialView(ContactListView, new AdminContactListModel
        {
            CustomerId = customerId,
            CanEdit = await CanEditCustomersAsync(),
            Contacts = await _contactService.GetForCustomerAsync(customerId)
        });
    }

    [HttpPost]
    public async Task<IActionResult> ContactSave(int customerId, ContactModel model)
    {
        if (!await CanEditCustomersAsync())
            return Failure(await _localizationService.GetResourceAsync("Admin.AccessDenied.Description"));

        if (await _customerService.GetCustomerByIdAsync(customerId) == null)
            return Failure(await TAsync("NotFound"));

        var admin = await _workContext.GetCurrentCustomerAsync();
        var errors = await _contactEditor.SaveAsync(customerId, model, admin.Id);
        return errors.Any() ? Failure(errors.ToArray()) : Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> ContactDelete(int customerId, int id)
    {
        if (!await CanEditCustomersAsync())
            return Failure(await _localizationService.GetResourceAsync("Admin.AccessDenied.Description"));

        return await _contactEditor.DeleteAsync(customerId, id)
            ? Json(new { success = true })
            : Failure(await TAsync("NotFound"));
    }

    #endregion

    #region Certificates

    [HttpGet]
    public async Task<IActionResult> CertificateList(int customerId)
    {
        if (!await CanViewCustomersAsync())
            return Forbid();

        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
            return NotFound();

        var certificates = await _taxExemptionService.GetForCustomerAsync(customerId);
        return PartialView(CertificateListView, new AdminCertificateListModel
        {
            CustomerId = customerId,
            CanEdit = await CanEditCustomersAsync(),
            IsTaxExempt = customer.IsTaxExempt,
            CurrentValid = await _taxExemptionService.GetCurrentValidAsync(customerId),
            HasPending = certificates.Any(c => c.State == CertificateState.PendingReview),
            Certificates = certificates,
            Today = _clock.Today
        });
    }

    private async Task<bool?> IsTaxExemptAsync(int certificateId)
    {
        var certificate = await _taxExemptionService.GetByIdAsync(certificateId);
        var customer = certificate == null ? null : await _customerService.GetCustomerByIdAsync(certificate.CustomerId);
        return customer?.IsTaxExempt;
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> CertificateUpload(int customerId, IFormFile? file, int exemptionReasonId, DateTime? expiresOn,
        string? certificateNumber, string? issuingState)
    {
        if (!await CanEditCustomersAsync())
            return Failure(await _localizationService.GetResourceAsync("Admin.AccessDenied.Description"));

        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
            return Failure(await TAsync("NotFound"));

        var admin = await _workContext.GetCurrentCustomerAsync();
        var errors = await _taxExemptionService.UploadAsync(customerId, file, exemptionReasonId, expiresOn, certificateNumber, issuingState,
            admin.Id, approveImmediately: true);
        if (errors.Any())
            return Failure(errors.ToArray());

        customer = await _customerService.GetCustomerByIdAsync(customerId);
        return Json(new { success = true, isTaxExempt = customer?.IsTaxExempt });
    }

    [HttpPost]
    public async Task<IActionResult> CertificateApprove(int id, DateTime? expiresOn, int exemptionReasonId, string? note)
    {
        if (!await CanEditCustomersAsync())
            return Failure(await _localizationService.GetResourceAsync("Admin.AccessDenied.Description"));

        var admin = await _workContext.GetCurrentCustomerAsync();
        var errors = await _taxExemptionService.ApproveAsync(id, expiresOn, exemptionReasonId, admin.Id, note);
        return errors.Any()
            ? Failure(errors.ToArray())
            : Json(new { success = true, isTaxExempt = await IsTaxExemptAsync(id) });
    }

    [HttpPost]
    public async Task<IActionResult> CertificateReject(int id, string? note)
    {
        if (!await CanEditCustomersAsync())
            return Failure(await _localizationService.GetResourceAsync("Admin.AccessDenied.Description"));

        var admin = await _workContext.GetCurrentCustomerAsync();
        var errors = await _taxExemptionService.RejectAsync(id, admin.Id, note);
        return errors.Any() ? Failure(errors.ToArray()) : Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> CertificateFile(int id)
    {
        if (!await CanViewCustomersAsync())
            return AccessDeniedView();

        var (certificate, download) = await _taxExemptionService.GetFileAsync(id);
        if (certificate == null || download == null)
            return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.DownloadBinary, certificate.ContentType, certificate.FileName);
    }

    #endregion
}
