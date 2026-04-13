// ============================================================
// WinConflu.NET — AppDbContext
// EF Core 10 + SQL Server + HierarchyId
// ============================================================

using Microsoft.EntityFrameworkCore;
using WinConflu.Models;

namespace WinConflu.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ── Wiki ─────────────────────────────────────────────────
    public DbSet<Page>       Pages      { get; set; }
    public DbSet<Revision>   Revisions  { get; set; }

    // ── テンプレート & インラインコメント ─────────────────
    public DbSet<PageTemplate>      PageTemplates      { get; set; }
    public DbSet<InlineAnnotation>  InlineAnnotations  { get; set; }
    public DbSet<AnnotationReply>   AnnotationReplies  { get; set; }

    // ── 通知 ─────────────────────────────────────────────
    public DbSet<AppNotification>   AppNotifications   { get; set; }

    // ── Boards ───────────────────────────────────────────────
    public DbSet<Project>             Projects            { get; set; }
    public DbSet<Issue>               Issues              { get; set; }
    public DbSet<IssueLabel>          IssueLabels         { get; set; }
    public DbSet<WorkflowDefinition>  WorkflowDefinitions { get; set; }
    public DbSet<WorkflowTransition>  WorkflowTransitions { get; set; }
    public DbSet<IssueDependency>     IssueDependencies   { get; set; }

    // ── 共通 ─────────────────────────────────────────────────
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<AuditLog>   AuditLogs   { get; set; }
    public DbSet<Comment>    Comments    { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Pages（Wiki記事） ────────────────────────────────
        modelBuilder.Entity<Page>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Title).HasMaxLength(500).IsRequired();
            entity.Property(p => p.Content).HasColumnType("nvarchar(max)");
            entity.Property(p => p.ContentFormat).HasMaxLength(20).HasDefaultValue("markdown");
            entity.Property(p => p.ContentText).HasColumnType("nvarchar(max)").HasDefaultValue("");

            // HierarchyId カラム（高速な階層ナビゲーション）
            entity.Property(p => p.Path)
                  .HasColumnType("hierarchyid")
                  .IsRequired();

            entity.Property(p => p.AdGroupSid).HasMaxLength(100);
            entity.Property(p => p.Slug).HasMaxLength(300);

            // HierarchyId インデックス（深さ優先 + 幅優先）
            entity.HasIndex(p => p.Path)
                  .HasDatabaseName("IX_Pages_Path");

            entity.HasIndex(p => new { p.Path, p.Id })
                  .HasDatabaseName("IX_Pages_Path_BreadthFirst");

            // フルテキスト検索インデックスは EF Migration の
            // MigrationBuilder.Sql() で手動追加（後述）

            entity.HasMany(p => p.Revisions)
                  .WithOne(r => r.Page)
                  .HasForeignKey(r => r.PageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.LinkedIssues)
                  .WithMany(i => i.LinkedPages)
                  .UsingEntity("PageIssueLinks");
        });

        // ── Revisions（更新履歴） ─────────────────────────────
        modelBuilder.Entity<Revision>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Content).HasColumnType("nvarchar(max)");
            entity.Property(r => r.ContentFormat).HasMaxLength(20).HasDefaultValue("markdown");
            entity.Property(r => r.AuthorSid).HasMaxLength(100);
            entity.HasIndex(r => r.PageId).HasDatabaseName("IX_Revisions_PageId");
            entity.HasIndex(r => r.CreatedAt).HasDatabaseName("IX_Revisions_CreatedAt");
        });

        // ── Projects（ボード） ────────────────────────────────
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Prefix).HasMaxLength(10).IsRequired();
            entity.Property(p => p.AdGroupOwnerSid).HasMaxLength(100);
            entity.HasIndex(p => p.Prefix).IsUnique();

            entity.HasMany(p => p.Issues)
                  .WithOne(i => i.Project)
                  .HasForeignKey(i => i.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Issues（タスク・チケット） ────────────────────────
        modelBuilder.Entity<Issue>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Title).HasMaxLength(500).IsRequired();
            entity.Property(i => i.Description).HasColumnType("nvarchar(max)");
            entity.Property(i => i.AssigneeSid).HasMaxLength(100);
            entity.Property(i => i.EpicColor).HasMaxLength(20);
            entity.Property(i => i.Status)
                  .HasConversion<string>().HasMaxLength(20);
            entity.Property(i => i.Priority)
                  .HasConversion<string>().HasMaxLength(10);
            entity.Property(i => i.Type)
                  .HasConversion<string>().HasMaxLength(20);

            // エピック / 親子階層
            entity.HasOne(i => i.ParentIssue)
                  .WithMany(i => i.SubIssues)
                  .HasForeignKey(i => i.ParentIssueId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(i => i.ProjectId).HasDatabaseName("IX_Issues_ProjectId");
            entity.HasIndex(i => i.Status).HasDatabaseName("IX_Issues_Status");
            entity.HasIndex(i => i.Type).HasDatabaseName("IX_Issues_Type");
            entity.HasIndex(i => i.AssigneeSid).HasDatabaseName("IX_Issues_AssigneeSid");
            entity.HasIndex(i => i.DueDate).HasDatabaseName("IX_Issues_DueDate");
            entity.HasIndex(i => i.ParentIssueId).HasDatabaseName("IX_Issues_ParentIssueId");
        });

        // ── WorkflowDefinition ────────────────────────────────
        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(w => w.Project).WithMany()
                  .HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(w => w.Transitions).WithOne(t => t.Workflow)
                  .HasForeignKey(t => t.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(w => new { w.ProjectId, w.IsDefault })
                  .HasDatabaseName("IX_WorkflowDefinitions_ProjectId_IsDefault");
        });

        // ── WorkflowTransition ────────────────────────────────
        modelBuilder.Entity<WorkflowTransition>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.RequiredRole).HasMaxLength(100);
            entity.Property(t => t.AutomationJson).HasColumnType("nvarchar(max)");
        });

        // ── IssueDependency ───────────────────────────────────
        modelBuilder.Entity<IssueDependency>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(d => d.SourceIssue).WithMany(i => i.Blocks)
                  .HasForeignKey(d => d.SourceIssueId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.TargetIssue).WithMany(i => i.BlockedBy)
                  .HasForeignKey(d => d.TargetIssueId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(d => d.SourceIssueId).HasDatabaseName("IX_IssueDependencies_SourceId");
            entity.HasIndex(d => d.TargetIssueId).HasDatabaseName("IX_IssueDependencies_TargetId");
        });

        // ── Attachments（添付ファイル） ───────────────────────
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.FileName).HasMaxLength(500).IsRequired();
            entity.Property(a => a.BlobUrl).HasMaxLength(2000).IsRequired();
            entity.Property(a => a.ContentType).HasMaxLength(200);
            entity.HasIndex(a => a.RelatedId).HasDatabaseName("IX_Attachments_RelatedId");
        });

        // ── AuditLog（操作ログ） ──────────────────────────────
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.UserSid).HasMaxLength(100);
            entity.Property(a => a.Action).HasMaxLength(50);
            entity.Property(a => a.EntityType).HasMaxLength(100);
            entity.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_AuditLogs_CreatedAt");
            entity.HasIndex(a => a.UserSid).HasDatabaseName("IX_AuditLogs_UserSid");
        });

        // ── Comments ─────────────────────────────────────────
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Body).HasColumnType("nvarchar(max)");
            entity.Property(c => c.AuthorSid).HasMaxLength(100);
            entity.HasIndex(c => c.RelatedId).HasDatabaseName("IX_Comments_RelatedId");
        });

        // ── PageTemplate ──────────────────────────────────────
        modelBuilder.Entity<PageTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Category).HasMaxLength(50);
            entity.Property(t => t.IconName).HasMaxLength(100);
            entity.Property(t => t.Content).HasColumnType("nvarchar(max)");
            entity.Property(t => t.VariablesJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(t => t.Category).HasDatabaseName("IX_PageTemplates_Category");
        });

        // ── InlineAnnotation ──────────────────────────────────
        modelBuilder.Entity<InlineAnnotation>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.SelectedText).HasMaxLength(1000);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(a => a.PageId).HasDatabaseName("IX_InlineAnnotations_PageId");
            entity.HasIndex(a => new { a.PageId, a.Status })
                  .HasDatabaseName("IX_InlineAnnotations_PageId_Status");
            entity.HasOne(a => a.Page).WithMany()
                  .HasForeignKey(a => a.PageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(a => a.Replies).WithOne(r => r.Annotation)
                  .HasForeignKey(r => r.AnnotationId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── AnnotationReply ───────────────────────────────────
        modelBuilder.Entity<AnnotationReply>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Body).HasColumnType("nvarchar(max)");
            entity.Property(r => r.AuthorSid).HasMaxLength(100);
            entity.HasIndex(r => r.AnnotationId)
                  .HasDatabaseName("IX_AnnotationReplies_AnnotationId");
        });

        // ── AppNotification ───────────────────────────────────
        modelBuilder.Entity<AppNotification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.RecipientSid).HasMaxLength(100).IsRequired();
            entity.Property(n => n.Title).HasMaxLength(200).IsRequired();
            entity.Property(n => n.Body).HasMaxLength(1000);
            entity.Property(n => n.LinkUrl).HasMaxLength(500);
            entity.Property(n => n.Kind).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(n => new { n.RecipientSid, n.IsRead })
                  .HasDatabaseName("IX_AppNotifications_RecipientSid_IsRead");
            entity.HasIndex(n => n.CreatedAt)
                  .HasDatabaseName("IX_AppNotifications_CreatedAt");
        });
    }
}
