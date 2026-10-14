using Nop.Core;
using Nop.Data;
using Nop.Plugin.Misc.XpsShip.Domain;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>Reads and writes the plugin's three tables</summary>
public class XpsShipDataService
{
    private readonly IRepository<XpsShipOrderFlag> _flagRepository;
    private readonly IRepository<XpsShipTrackingMap> _mapRepository;
    private readonly IRepository<XpsShipWebhookLog> _logRepository;

    public XpsShipDataService(IRepository<XpsShipOrderFlag> flagRepository,
        IRepository<XpsShipTrackingMap> mapRepository,
        IRepository<XpsShipWebhookLog> logRepository)
    {
        _flagRepository = flagRepository;
        _mapRepository = mapRepository;
        _logRepository = logRepository;
    }

    #region Order flags

    public async Task<IList<XpsShipOrderFlag>> GetFlagsAsync(IEnumerable<Guid> orderGuids)
    {
        var guids = orderGuids.Distinct().ToList();
        if (guids.Count == 0)
            return new List<XpsShipOrderFlag>();

        return await _flagRepository.Table.Where(f => guids.Contains(f.OrderGuid)).ToListAsync();
    }

    public async Task<IPagedList<XpsShipOrderFlag>> SearchFlagsAsync(string? flag, string? orderNumber, int pageIndex, int pageSize)
    {
        return await _flagRepository.GetAllPagedAsync(query =>
        {
            if (!string.IsNullOrEmpty(flag))
                query = query.Where(f => f.Flag == flag);
            if (!string.IsNullOrWhiteSpace(orderNumber))
                query = query.Where(f => f.CustomOrderNumber == orderNumber.Trim());

            return query.OrderByDescending(f => f.CreatedOnUtc);
        }, pageIndex, pageSize);
    }

    public async Task<XpsShipOrderFlag?> GetFlagAsync(Guid orderGuid, string flag) =>
        await _flagRepository.Table.Where(f => f.OrderGuid == orderGuid && f.Flag == flag).FirstOrDefaultAsync();

    public Task<XpsShipOrderFlag> GetFlagByIdAsync(int id) => _flagRepository.GetByIdAsync(id);

    public Task InsertFlagAsync(XpsShipOrderFlag flag) => _flagRepository.InsertAsync(flag, false);

    public Task DeleteFlagAsync(XpsShipOrderFlag flag) => _flagRepository.DeleteAsync(flag, false);

    public Task<int> DeleteFlagsAsync(Guid orderGuid, string flag) =>
        _flagRepository.DeleteAsync(f => f.OrderGuid == orderGuid && f.Flag == flag);

    #endregion

    #region Tracking map

    public async Task<XpsShipTrackingMap?> FindMapAsync(Guid orderGuid, string primaryTrackingNumber) =>
        await _mapRepository.Table
            .Where(m => m.OrderGuid == orderGuid && m.PrimaryTrackingNumber == primaryTrackingNumber)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync();

    public async Task<IList<XpsShipTrackingMap>> GetMapsForOrderAsync(Guid orderGuid) =>
        await _mapRepository.Table.Where(m => m.OrderGuid == orderGuid).OrderBy(m => m.Id).ToListAsync();

    public async Task<IPagedList<XpsShipTrackingMap>> SearchMapsAsync(string? orderNumber, int pageIndex, int pageSize)
    {
        return await _mapRepository.GetAllPagedAsync(query =>
        {
            if (!string.IsNullOrWhiteSpace(orderNumber))
                query = query.Where(m => m.CustomOrderNumber == orderNumber.Trim());

            return query.OrderByDescending(m => m.Id);
        }, pageIndex, pageSize);
    }

    public Task<XpsShipTrackingMap> GetMapByIdAsync(int id) => _mapRepository.GetByIdAsync(id);

    public Task InsertMapAsync(XpsShipTrackingMap map) => _mapRepository.InsertAsync(map, false);

    public Task UpdateMapAsync(XpsShipTrackingMap map) => _mapRepository.UpdateAsync(map, false);

    public Task DeleteMapAsync(XpsShipTrackingMap map) => _mapRepository.DeleteAsync(map, false);

    #endregion

    #region Webhook log

    public Task InsertLogAsync(XpsShipWebhookLog log)
    {
        log.CreatedOnUtc = DateTime.UtcNow;
        if (log.RequestBody?.Length > XpsShipDefaults.RequestBodyMaxLength)
            log.RequestBody = log.RequestBody.Substring(0, XpsShipDefaults.RequestBodyMaxLength);
        log.ResponseSummary = Cap(log.ResponseSummary, 1000);
        log.Message = Cap(log.Message, 4000);

        return _logRepository.InsertAsync(log, false);
    }

    public Task UpdateLogAsync(XpsShipWebhookLog log)
    {
        log.ResponseSummary = Cap(log.ResponseSummary, 1000);
        log.Message = Cap(log.Message, 4000);
        return _logRepository.UpdateAsync(log, false);
    }

    public async Task<IPagedList<XpsShipWebhookLog>> SearchLogAsync(string? endpoint, string? orderNumber, bool errorsOnly,
        int pageIndex, int pageSize)
    {
        return await _logRepository.GetAllPagedAsync(query =>
        {
            if (!string.IsNullOrEmpty(endpoint))
                query = query.Where(l => l.Endpoint == endpoint);
            if (!string.IsNullOrWhiteSpace(orderNumber))
                query = query.Where(l => l.CustomOrderNumber == orderNumber.Trim());
            if (errorsOnly)
                query = query.Where(l => l.HttpStatus >= 400);

            return query.OrderByDescending(l => l.Id);
        }, pageIndex, pageSize);
    }

    public Task<XpsShipWebhookLog> GetLogByIdAsync(int id) => _logRepository.GetByIdAsync(id);

    /// <summary>
    /// Writes a catalog warning unless the same one was written for the order in the last 30 days.
    /// List Orders runs every few minutes, so without this every poll would repeat every warning.
    /// </summary>
    public async Task AddWarningOnceAsync(Guid orderGuid, string customOrderNumber, string message)
    {
        var since = DateTime.UtcNow.AddDays(-XpsShipDefaults.WarningsDays);
        var exists = await _logRepository.Table.AnyAsync(l => l.Endpoint == XpsShipDefaults.EndpointWarning
            && l.OrderGuid == orderGuid && l.Message == message && l.CreatedOnUtc >= since);
        if (exists)
            return;

        await InsertLogAsync(new XpsShipWebhookLog
        {
            Endpoint = XpsShipDefaults.EndpointWarning,
            OrderGuid = orderGuid,
            CustomOrderNumber = customOrderNumber,
            Message = message
        });
    }

    public async Task<int> PurgeLogAsync(int retentionDays)
    {
        var before = DateTime.UtcNow.AddDays(-Math.Max(retentionDays, 1));
        return await _logRepository.DeleteAsync(l => l.CreatedOnUtc < before);
    }

    #endregion

    private static string? Cap(string? value, int max) => value?.Length > max ? value.Substring(0, max) : value;
}
