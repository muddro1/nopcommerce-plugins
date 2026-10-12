using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.CustomerProfile.Services;

namespace Nop.Plugin.Misc.CustomerProfile.Controllers;

// Export of all plugin data, and the deliberate "delete all plugin data" (uninstall keeps the data)
public partial class CustomerProfileAdminController
{
    public const string DeleteConfirmationWord = "DELETE";

    [HttpGet]
    public async Task<IActionResult> Export(CertificateFilter filter = CertificateFilter.All, string? dealership = null, string? customer = null)
    {
        if (!await CanViewCustomersAsync())
            return AccessDeniedView();

        // Built in a temp file, not memory: certificate files can add up to hundreds of MB. Removed when the response closes.
        var path = Path.Combine(Path.GetTempPath(), $"customer-profile-export-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await _dataService.WriteExportAsync(stream, filter, dealership, customer);
            stream.Position = 0;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        return File(stream, "application/zip", $"customer-profile-{_clock.Today:yyyy-MM-dd}.zip");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAllData(string? confirmation)
    {
        if (!await CanConfigureAsync())
            return AccessDeniedView();

        if (!string.Equals(confirmation?.Trim(), DeleteConfirmationWord, StringComparison.Ordinal))
        {
            _notificationService.ErrorNotification(string.Format(await TAsync("Data.Delete.NotConfirmed"), DeleteConfirmationWord));
            return RedirectToAction(nameof(Configure));
        }

        var deleted = await _dataService.DeleteAllAsync();
        _notificationService.SuccessNotification(string.Format(await TAsync("Data.Delete.Done"),
            deleted.Certificates, deleted.Profiles, deleted.Contacts, deleted.Net30Orders));
        return RedirectToAction(nameof(Configure));
    }
}
