using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Models;
using DayTrace.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("admin/messaging")]
public class AdminMessagingController : ControllerBase
{
    private const int ActiveAudiencePageSize = 500;
    private const int DefaultListLimit = 20;
    private const int MaxListLimit = 100;

    private readonly IUserRepository _userRepo;
    private readonly IDeliveryAttemptRepository _deliveryAttemptRepo;
    private readonly IAdminBroadcastCampaignRepository _broadcastCampaignRepo;
    private readonly IAdminAuditService _adminAuditService;
    private readonly DayTraceDbContext _dbContext;

    public AdminMessagingController(
        IUserRepository userRepo,
        IDeliveryAttemptRepository deliveryAttemptRepo,
        IAdminBroadcastCampaignRepository broadcastCampaignRepo,
        IAdminAuditService adminAuditService,
        DayTraceDbContext dbContext)
    {
        _userRepo = userRepo;
        _deliveryAttemptRepo = deliveryAttemptRepo;
        _broadcastCampaignRepo = broadcastCampaignRepo;
        _adminAuditService = adminAuditService;
        _dbContext = dbContext;
    }

    /// <summary>
    /// POST /admin/messaging/broadcast — Queue broadcast message for active users or active users with reminders.
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] AdminBroadcastRequest? request)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        if (role.Equals("analyst", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { error = "forbidden", message = "Analyst role cannot send broadcasts" });

        var text = request?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { error = "invalid_request", message = "text is required" });

        if (text.Length > 4096)
            return BadRequest(new { error = "invalid_request", message = "text is too long (max 4096)" });

        var audience = request?.Audience?.Trim().ToLowerInvariant();
        if (audience is not ("active" or "reminders"))
            return BadRequest(new { error = "invalid_request", message = "audience must be 'active' or 'reminders'" });

        var ct = HttpContext.RequestAborted;
        var campaign = new AdminBroadcastCampaign
        {
            CreatedByAdminUserId = admin.Id,
            Audience = audience,
            Text = text,
            Status = "queued",
            CreatedAt = DateTime.UtcNow,
            QueuedAt = DateTime.UtcNow
        };

        int queuedCount;

        await using (var tx = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            await _broadcastCampaignRepo.CreateAsync(campaign, ct);
            queuedCount = await EnqueueAudienceAttemptsAsync(campaign, ct);
            campaign.QueuedAt = DateTime.UtcNow;
            await _broadcastCampaignRepo.UpdateAsync(campaign, ct);
            await tx.CommitAsync(ct);
        }

        await _adminAuditService.LogSuccessAsync(admin.Id, "create_broadcast_campaign", "broadcast_campaign", campaign.Id.ToString());

        return Ok(new
        {
            campaign_id = campaign.Id,
            status = campaign.Status,
            queued_count = queuedCount,
            audience = campaign.Audience
        });
    }

    /// <summary>
    /// GET /admin/messaging/broadcasts — List queued/sent admin broadcast campaigns with delivery aggregates.
    /// </summary>
    [HttpGet("broadcasts")]
    public async Task<IActionResult> ListBroadcasts(
        [FromQuery] int limit = DefaultListLimit,
        [FromQuery] int offset = 0,
        [FromQuery] string? audience = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        limit = Math.Clamp(limit, 1, MaxListLimit);
        offset = Math.Max(offset, 0);
        audience = NormalizeAudienceOrNull(audience);
        if (audience == "__invalid__")
            return BadRequest(new { error = "invalid_request", message = "audience must be 'active' or 'reminders'" });

        var ct = HttpContext.RequestAborted;
        var campaigns = await _broadcastCampaignRepo.AdminListAsync(limit, offset, audience, ct);
        var total = await _broadcastCampaignRepo.AdminCountAsync(audience, ct);
        var ids = campaigns.Select(c => c.Id).ToArray();
        var statsMap = await _deliveryAttemptRepo.GetAdminBroadcastStatsByCampaignIdsAsync(ids, ct);
        var responseItems = new List<object>(campaigns.Count);

        foreach (var campaign in campaigns)
        {
            var stats = statsMap.TryGetValue(campaign.Id, out var found)
                ? found
                : new BroadcastCampaignDeliveryStats { CampaignId = campaign.Id };

            var campaignView = await EnsureCampaignStateAsync(campaign, stats, ct);
            responseItems.Add(ToCampaignListResponse(campaignView, stats));
        }

        await _adminAuditService.LogSuccessAsync(admin.Id, "list_broadcast_campaigns", "broadcast_campaign", null);

        return Ok(new
        {
            items = responseItems,
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// GET /admin/messaging/broadcasts/{id} — Broadcast campaign detail with delivery aggregates.
    /// </summary>
    [HttpGet("broadcasts/{id:long}")]
    public async Task<IActionResult> GetBroadcast(long id)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var ct = HttpContext.RequestAborted;
        var campaign = await _broadcastCampaignRepo.GetByIdAsync(id, ct);
        if (campaign == null)
            return NotFound(new { error = "not_found" });

        var stats = await _deliveryAttemptRepo.GetAdminBroadcastStatsAsync(id, ct);
        campaign = await EnsureCampaignStateAsync(campaign, stats, ct);

        await _adminAuditService.LogSuccessAsync(admin.Id, "get_broadcast_campaign", "broadcast_campaign", id.ToString());

        return Ok(new
        {
            id = campaign.Id,
            status = campaign.Status,
            audience = campaign.Audience,
            text = campaign.Text,
            text_preview = BuildTextPreview(campaign.Text),
            created_by_admin_user_id = campaign.CreatedByAdminUserId,
            created_by_admin_id = campaign.CreatedByAdminUserId,
            created_at = campaign.CreatedAt,
            queued_at = campaign.QueuedAt,
            completed_at = campaign.CompletedAt,
            queued_count = stats.Total,
            pending_count = stats.Pending,
            sent_count = stats.Sent,
            failed_count = stats.Failed,
            terminal_failed_count = stats.TerminalFailed,
            counts = ToCountsResponse(stats)
        });
    }

    private async Task<int> EnqueueAudienceAttemptsAsync(AdminBroadcastCampaign campaign, CancellationToken ct)
    {
        if (campaign.Audience == "reminders")
        {
            var users = await _userRepo.GetActiveUsersWithRemindersAsync(ct);
            var now = DateTime.UtcNow;
            var attempts = users.Select(u => BuildBroadcastAttempt(u.Id, campaign.Id, now)).ToList();
            await _deliveryAttemptRepo.CreateRangeAsync(attempts, ct);
            return attempts.Count;
        }

        var total = 0;
        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            var users = await _userRepo.GetAllAsync(ActiveAudiencePageSize, offset, search: null, status: "active", ct: ct);
            if (users.Count == 0)
                break;

            var now = DateTime.UtcNow;
            var attempts = users.Select(u => BuildBroadcastAttempt(u.Id, campaign.Id, now)).ToList();
            await _deliveryAttemptRepo.CreateRangeAsync(attempts, ct);

            total += attempts.Count;

            if (users.Count < ActiveAudiencePageSize)
                break;

            offset += users.Count;
        }

        return total;
    }

    private static DeliveryAttempt BuildBroadcastAttempt(long userId, long campaignId, DateTime nowUtc) => new()
    {
        UserId = userId,
        DeliveryType = "admin_broadcast",
        ReferenceId = campaignId,
        AttemptNumber = 1,
        Status = "pending",
        ScheduledAt = nowUtc,
        CreatedAt = nowUtc
    };

    private static string BuildTextPreview(string text)
    {
        const int maxPreview = 120;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxPreview)
            return normalized;

        return $"{normalized[..maxPreview]}...";
    }

    private static string? NormalizeAudienceOrNull(string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
            return null;

        var normalized = audience.Trim().ToLowerInvariant();
        return normalized is "active" or "reminders" ? normalized : "__invalid__";
    }

    private async Task<AdminBroadcastCampaign> EnsureCampaignStateAsync(
        AdminBroadcastCampaign campaign,
        BroadcastCampaignDeliveryStats stats,
        CancellationToken ct)
    {
        var derivedStatus = DeriveCampaignStatus(stats);
        var shouldBeCompleted = IsTerminalCampaignStatus(derivedStatus);

        var normalizedQueuedAt = campaign.QueuedAt == default ? campaign.CreatedAt : campaign.QueuedAt;
        var completedAt = campaign.CompletedAt;

        if (shouldBeCompleted)
        {
            completedAt ??= DateTime.UtcNow;
        }
        else
        {
            completedAt = null;
        }

        var requiresUpdate =
            campaign.QueuedAt != normalizedQueuedAt ||
            !string.Equals(campaign.Status, derivedStatus, StringComparison.Ordinal) ||
            campaign.CompletedAt != completedAt;

        if (!requiresUpdate)
            return campaign;

        campaign.QueuedAt = normalizedQueuedAt;
        campaign.Status = derivedStatus;
        campaign.CompletedAt = completedAt;
        await _broadcastCampaignRepo.UpdateAsync(campaign, ct);

        return campaign;
    }

    private static string DeriveCampaignStatus(BroadcastCampaignDeliveryStats stats)
    {
        if (stats.Total <= 0)
            return "queued";

        if (stats.Pending > 0 || stats.Failed > 0)
        {
            if (stats.Sent > 0 || stats.TerminalFailed > 0)
                return "processing";

            return "queued";
        }

        if (stats.Sent > 0 && stats.TerminalFailed > 0)
            return "partial_failed";

        if (stats.Sent > 0)
            return "completed";

        if (stats.TerminalFailed > 0)
            return "failed";

        return "queued";
    }

    private static bool IsTerminalCampaignStatus(string status)
        => status is "completed" or "partial_failed" or "failed";

    private static object ToCountsResponse(BroadcastCampaignDeliveryStats stats) => new
    {
        total = stats.Total,
        pending = stats.Pending,
        sent = stats.Sent,
        failed = stats.Failed,
        terminal_failed = stats.TerminalFailed
    };

    private static object ToCampaignListResponse(AdminBroadcastCampaign c, BroadcastCampaignDeliveryStats stats) => new
    {
        id = c.Id,
        status = c.Status,
        audience = c.Audience,
        created_by_admin_user_id = c.CreatedByAdminUserId,
        created_by_admin_id = c.CreatedByAdminUserId,
        created_at = c.CreatedAt,
        queued_at = c.QueuedAt,
        completed_at = c.CompletedAt,
        text_preview = BuildTextPreview(c.Text),
        text_length = c.Text.Length,
        queued_count = stats.Total,
        pending_count = stats.Pending,
        sent_count = stats.Sent,
        failed_count = stats.Failed,
        terminal_failed_count = stats.TerminalFailed,
        counts = ToCountsResponse(stats)
    };

    public sealed class AdminBroadcastRequest
    {
        public string? Text { get; set; }
        public string? Audience { get; set; }
    }
}
