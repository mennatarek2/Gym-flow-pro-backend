namespace GMS.Application.Jobs;

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;

/// <summary>
/// Expires overdue pending guest_pass invitations (VisitDate &lt; Cairo today → expired).
/// Lives in Application because it calls <see cref="IInvitationService"/>.
/// </summary>
public class GuestPassExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuestPassExpiryJob> _logger;

    public GuestPassExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<GuestPassExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ExecuteAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var count = await invitations.ExpireOverdueGuestPassesAsync();
        _logger.LogInformation(
            "GuestPassExpiryJob: expired {Count} pending guest_pass invitation(s)", count);
    }
}
