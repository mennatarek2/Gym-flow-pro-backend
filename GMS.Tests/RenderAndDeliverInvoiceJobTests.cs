namespace GMS.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class RenderAndDeliverInvoiceJobTests
{
    private class FlakyWhatsAppService : IWhatsAppService
    {
        public int DocumentSendAttempts { get; private set; }

        public Task SendExpiryReminderAsync(Guid memberId, int daysLeft) => Task.CompletedTask;
        public Task SendExpiryReminderAsync(string phone, string memberName, int daysLeft) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(Guid memberId, string discountCode) => Task.CompletedTask;
        public Task SendBirthdayGreetingAsync(string phone, string memberName, string discountCode) => Task.CompletedTask;
        public Task SendClassReminderAsync(Guid memberId, string className, DateTime classTime) => Task.CompletedTask;
        public Task SendClassReminderAsync(string phone, string className, DateTime startTime) => Task.CompletedTask;
        public Task SendGuestInvitationAsync(string phoneNumber, string guestName, string gymName, DateOnly visitDate) => Task.CompletedTask;
        public Task SendRenewalConfirmationAsync(string phone, string memberName, DateTime newExpiry) => Task.CompletedTask;

        public Task SendDocumentAsync(string phone, string memberName, string documentUrl, string caption, string captionAr)
        {
            DocumentSendAttempts++;
            if (DocumentSendAttempts < 3)
                throw new InvalidOperationException($"Simulated transient WhatsApp failure #{DocumentSendAttempts}");

            return Task.CompletedTask;
        }

        public Task SendTemplateAsync(string phone, string templateName, Dictionary<string, string> parameters) => Task.CompletedTask;
    }

    private class CountingInvoicePdfRenderer : IInvoicePdfRenderer
    {
        public int RenderCount { get; private set; }

        public byte[] Render(InvoicePdfModel model)
        {
            RenderCount++;
            return new byte[] { 1, 2, 3 };
        }
    }

    private class NoOpFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(false);
    }

    private class NoOpPushNotificationService : IPushNotificationService
    {
        public Task SendToDeviceAsync(string fcmToken, string title, string body) => Task.CompletedTask;
        public Task SendToTopicAsync(string topic, string title, string body) => Task.CompletedTask;
    }

    /// <summary>
    /// Hangfire's [AutomaticRetry(Attempts = 3)] re-invokes Execute with the same arguments when it
    /// throws. Spinning up real Hangfire storage/workers to prove that would be far heavier than
    /// the actual behavior under test, so this drives the same re-invocation manually.
    /// </summary>
    [Fact]
    public async Task Execute_WhatsAppFailsTwiceThenSucceeds_JobEventuallySucceeds_WithoutReRenderingPdf()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Tenant", "Africa/Cairo");

        await using var ctx = new GymFlowProDbContext(options, tenantContext);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            Type = "invoice",
            InvoiceNumber = "INV-2026-000001",
            MemberNameSnapshot = "Test Member",
            MemberPhoneSnapshot = "+201000000000",
            LinesSnapshot = "[]",
            Subtotal = 100m,
            Total = 100m,
            IssuedAt = DateTime.UtcNow,
            Status = "issued"
        };
        ctx.Invoices.Add(invoice);
        await ctx.SaveChangesAsync();

        var flakyWhatsApp = new FlakyWhatsAppService();
        var pdfRenderer = new CountingInvoicePdfRenderer();
        var notificationService = new NotificationService(
            ctx,
            new NoOpPushNotificationService(),
            NullLogger<NotificationService>.Instance);
        var auditService = new AuditService(ctx, new HttpContextAccessor(), tenantContext, NullLogger<AuditService>.Instance);

        var job = new RenderAndDeliverInvoiceJob(
            ctx, pdfRenderer, new NoOpFileStorageService(), flakyWhatsApp, notificationService,
            NullLogger<RenderAndDeliverInvoiceJob>.Instance);

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            lastException = await Record.ExceptionAsync(() => job.Execute(invoice.Id));
            if (lastException == null)
                break;
        }

        Assert.Null(lastException);
        Assert.Equal(3, flakyWhatsApp.DocumentSendAttempts);
        Assert.Equal(1, pdfRenderer.RenderCount); // not re-rendered on the retried attempts

        var reloaded = await ctx.Invoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.False(string.IsNullOrWhiteSpace(reloaded.PdfUrl));
    }
}
