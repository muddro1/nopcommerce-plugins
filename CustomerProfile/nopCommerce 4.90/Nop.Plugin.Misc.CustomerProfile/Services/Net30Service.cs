using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;

namespace Nop.Plugin.Misc.CustomerProfile.Services;

public interface INet30Service
{
    Task<Net30Order?> GetByOrderIdAsync(int orderId);
    Task InsertAsync(Net30Order net30Order);
    Task UpdateAsync(Net30Order net30Order);

    /// <summary>Net 30 orders not yet paid, cancelled or deleted</summary>
    Task<IList<Net30Snapshot>> GetUnpaidSnapshotsAsync();

    Task<IPagedList<Net30Row>> SearchUnpaidAsync(bool overdueOnly, int pageIndex, int pageSize);

    /// <summary>Daily task: emails the overdue reminder for one order, once</summary>
    Task SendOverdueAsync(int orderId);
}

public class Net30Service : INet30Service
{
    private readonly IRepository<Net30Order> _net30Repository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CustomerBusinessProfile> _profileRepository;
    private readonly IStoreClock _clock;
    private readonly ICustomerProfileMessageService _messageService;

    public Net30Service(IRepository<Net30Order> net30Repository,
        IRepository<Order> orderRepository,
        IRepository<Customer> customerRepository,
        IRepository<CustomerBusinessProfile> profileRepository,
        IStoreClock clock,
        ICustomerProfileMessageService messageService)
    {
        _messageService = messageService;
        _net30Repository = net30Repository;
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _profileRepository = profileRepository;
        _clock = clock;
    }

    public async Task<Net30Order?> GetByOrderIdAsync(int orderId) =>
        orderId <= 0 ? null : await _net30Repository.Table.FirstOrDefaultAsync(n => n.OrderId == orderId);

    public Task InsertAsync(Net30Order net30Order) => _net30Repository.InsertAsync(net30Order);

    public Task UpdateAsync(Net30Order net30Order) => _net30Repository.UpdateAsync(net30Order);

    public async Task<IList<Net30Snapshot>> GetUnpaidSnapshotsAsync()
    {
        var rows = await (
            from n in _net30Repository.Table
            join o in _orderRepository.Table on n.OrderId equals o.Id
            where !o.Deleted && o.PaymentStatusId != (int)PaymentStatus.Paid && o.OrderStatusId != (int)OrderStatus.Cancelled
            select n).ToListAsync();

        return rows.Select(n => new Net30Snapshot(n.OrderId, n.CustomerId, n.DueOn,
            IsPaid: false, IsCancelled: false, n.OverdueNoticeSentOnUtc)).ToList();
    }

    public async Task<IPagedList<Net30Row>> SearchUnpaidAsync(bool overdueOnly, int pageIndex, int pageSize)
    {
        var today = _clock.Today;

        var query =
            from n in _net30Repository.Table
            join o in _orderRepository.Table on n.OrderId equals o.Id
            join cu in _customerRepository.Table on n.CustomerId equals cu.Id
            join p in _profileRepository.Table on n.CustomerId equals p.CustomerId into profiles
            from p in profiles.DefaultIfEmpty()
            where !o.Deleted && o.PaymentStatusId != (int)PaymentStatus.Paid && o.OrderStatusId != (int)OrderStatus.Cancelled
            select new { n, o, cu, Dealership = p == null ? null : p.BmwDealership };

        if (overdueOnly)
            query = query.Where(x => x.n.DueOn < today);

        var page = await query.OrderBy(x => x.n.DueOn).ThenBy(x => x.o.Id).ToPagedListAsync(pageIndex, pageSize);
        var rows = page.Select(x => new Net30Row(x.n, x.o.CustomOrderNumber, x.o.OrderTotal, x.o.CreatedOnUtc,
            x.cu.Email, $"{x.cu.FirstName} {x.cu.LastName}".Trim(), x.cu.Company, x.Dealership)).ToList();

        return new PagedList<Net30Row>(rows, page.PageIndex, page.PageSize, page.TotalCount);
    }

    public async Task SendOverdueAsync(int orderId)
    {
        var net30 = await GetByOrderIdAsync(orderId);
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (net30 is not { OverdueNoticeSentOnUtc: null } || order == null || order.Deleted
            || order.PaymentStatus == PaymentStatus.Paid || order.OrderStatus == OrderStatus.Cancelled)
            return;

        // Send, then stamp: a failure in between re-sends once rather than never
        await _messageService.SendNet30OverdueAsync(net30, order);
        net30.OverdueNoticeSentOnUtc = _clock.UtcNow;
        await _net30Repository.UpdateAsync(net30);
    }
}
