using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.XpsShip.Services;

/// <summary>Daily: deletes webhook log rows (which hold customer addresses) older than LogRetentionDays</summary>
public class PurgeWebhookLogTask : IScheduleTask
{
    private readonly XpsShipDataService _dataService;
    private readonly XpsShipSettings _settings;

    public PurgeWebhookLogTask(XpsShipDataService dataService, XpsShipSettings settings)
    {
        _dataService = dataService;
        _settings = settings;
    }

    public async Task ExecuteAsync() => await _dataService.PurgeLogAsync(_settings.LogRetentionDays);
}
