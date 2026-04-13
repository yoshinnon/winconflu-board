// ============================================================
// WinConflu.NET + Boards — エントリポイント (Program.cs)
// .NET 10 / Blazor Server / EF Core 10 / MudBlazor 9
// ============================================================

using Azure.Identity;
using Azure.Storage.Blobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WinConflu.Data;
using WinConflu.Services;
using WinConflu.Services.Graph;

var builder = WebApplication.CreateBuilder(args);

// ── Key Vault 統合（本番のみ） ────────────────────────────
if (builder.Environment.IsProduction())
{
    var kvUri = builder.Configuration["KeyVaultUri"]
        ?? throw new InvalidOperationException("KeyVaultUri が未設定です。");
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
}

// ── Windows 認証 ──────────────────────────────────────────
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(o => o.FallbackPolicy = o.DefaultPolicy);

// ── EF Core + SQL Server + HierarchyId ────────────────────
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.UseHierarchyId();
            sql.CommandTimeout(30);
            sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
        })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution));

// ── Azure Blob Storage ────────────────────────────────────
builder.Services.AddSingleton(_ => new BlobServiceClient(
    new Uri(builder.Configuration["Storage:BlobEndpoint"]
            ?? throw new InvalidOperationException("Storage:BlobEndpoint が未設定です。")),
    new DefaultAzureCredential()));

// ── Hangfire（バックグラウンドジョブ / 通知エンジン） ─────
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout       = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout   = TimeSpan.FromMinutes(5),
            QueuePollInterval            = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks           = true
        }));
builder.Services.AddHangfireServer();

// ── Microsoft Graph ───────────────────────────────────────
builder.Services.AddScoped<GraphServiceClientFactory>();
builder.Services.AddScoped<IPresenceService, TeamsPresenceService>();
builder.Services.AddScoped<ICalendarService, OutlookCalendarService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();

// ── アプリケーションサービス ──────────────────────────────
builder.Services.AddScoped<IMarkdownService, MarkdownService>();
builder.Services.AddScoped<IDiffService, DiffService>();
builder.Services.AddScoped<IProseMirrorService, ProseMirrorService>();
builder.Services.AddScoped<IWikiService, WikiService>();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<ISearchService, FullTextSearchService>();
builder.Services.AddScoped<IAttachmentService, BlobAttachmentService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IInlineAnnotationService, InlineAnnotationService>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddSingleton<ICacheService, CacheService>();

// ── MemoryCache + AD グループ ─────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAdGroupService, AdGroupService>();
builder.Services.AddHttpContextAccessor();

// ── MudBlazor 9 ───────────────────────────────────────────
builder.Services.AddMudServices(cfg =>
{
    cfg.SnackbarConfiguration.PositionClass          = MudBlazor.Defaults.Classes.Position.BottomRight;
    cfg.SnackbarConfiguration.ShowTransitionDuration = 300;
    cfg.SnackbarConfiguration.HideTransitionDuration = 300;
});

// ── Blazor Server + SignalR ───────────────────────────────
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR(opts =>
{
    opts.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    opts.EnableDetailedErrors      = builder.Environment.IsDevelopment();
});
if (builder.Environment.IsProduction())
    builder.Services.AddSignalR()
        .AddAzureSignalR(builder.Configuration["Azure:SignalR:ConnectionString"]);

// ── Application Insights ──────────────────────────────────
builder.Services.AddApplicationInsightsTelemetry(opts =>
    opts.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);

// ── ヘルスチェック ────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!,
                  name: "sql-database", tags: ["db", "ready"])
    .AddAzureBlobStorage(builder.Configuration["Storage:BlobEndpoint"]!,
                         name: "blob-storage", tags: ["storage", "ready"]);

// ── CORS（Teams タブ組み込み用） ──────────────────────────
builder.Services.AddCors(o => o.AddPolicy("TeamsPolicy", p =>
    p.WithOrigins("https://teams.microsoft.com").AllowAnyMethod().AllowAnyHeader()));

// ════════════════════════════════════════════════════════════
var app = builder.Build();
// ════════════════════════════════════════════════════════════

if (app.Environment.IsDevelopment())
{
    using var scope  = app.Services.CreateScope();
    var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var tmplSvc      = scope.ServiceProvider.GetRequiredService<ITemplateService>();
    await tmplSvc.EnsureBuiltInSeedAsync();
    app.UseDeveloperExceptionPage();
}
else
{
    using var scope = app.Services.CreateScope();
    var tmplSvc     = scope.ServiceProvider.GetRequiredService<ITemplateService>();
    await tmplSvc.EnsureBuiltInSeedAsync();
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("TeamsPolicy");
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire ダッシュボード（開発・管理者のみ）
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});

// 定期ジョブの登録
using (var scope = app.Services.CreateScope())
{
    var notifSvc = scope.ServiceProvider.GetRequiredService<INotificationService>();
    notifSvc.ScheduleDueDateReminders();
    notifSvc.ScheduleOverdueCheck();

    // 毎週日曜 2:00 にインデックス断片化チェック＆再構築
    RecurringJob.AddOrUpdate<IPerformanceService>(
        "index-rebuild-weekly",
        svc => svc.RebuildFragmentedIndexesAsync(30, CancellationToken.None),
        "0 2 * * 0");
}

// ヘルスチェックエンドポイント
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            status  = report.Status.ToString(),
            checks  = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
            version = typeof(Program).Assembly.GetName().Version?.ToString()
        }));
    }
});

// SignalR Hub（通知プッシュ用）
app.MapHub<NotificationHub>("/hubs/notifications");

// Blazor Server
app.MapRazorComponents<WinConflu.Components.App>().AddInteractiveServerRenderMode();

await app.RunAsync();

// ── Hangfire 認可フィルタ（管理者のみダッシュボードにアクセス可） ──
public class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext ctx)
    {
        var http = ctx.GetHttpContext();
        // Windows 認証済みユーザーにのみ表示（本番は AD グループ検証を追加推奨）
        return http.User.Identity?.IsAuthenticated == true;
    }
}
