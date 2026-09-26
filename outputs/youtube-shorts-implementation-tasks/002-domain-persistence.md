# Plan 002: مدل دامنه و persistence قابل ممیزی

## وابستگی

Plan 001 باید DONE باشد. ابتدا commandهای واقعی و convention migration/test را از README مخزن بخوانید.

## هدف

داده‌های تولید محتوا باید versioned، قابل ردگیری و قابل query باشند. فایل media نباید در PostgreSQL ذخیره شود؛ فقط metadata و object key نگهداری می‌شود.

## Scope

- `Shorts.Domain`: aggregateها، enumها و ruleهای پایه
- `Shorts.Infrastructure`: EF Core mapping و migration اولیه
- migration و testهای persistence

## موجودیت‌های لازم

- `TopicCandidate`: URL canonical، creator، published time، ViralScore و lifecycle state.
- `SourceItem` و `ResearchPack`: منبع، excerpt، reliability، facts و uncertainty.
- `Production`، `ScriptVersion`، `Asset`، `AssetUsage`، `ReviewDecision`، `PublishJob` و `AuditEvent`.
- `RightsStatus`: Owned، Licensed، PlatformRemix، PermissionRequested، PermissionGranted، CommentaryRisk، Unknown، Rejected.

## مراحل

1. aggregate boundary را طوری بسازید که Production مالک script versions، asset usages و review decisionها باشد. Candidate می‌تواند چند production داشته باشد.
2. constraints database را اضافه کنید: canonical URL یکتا، script version یکتا در production، و جلوگیری از AssetUsage بدون Production و Asset.
3. timestampها UTC و audit eventها append-only باشند.
4. migration اولیه و integration test با database موقت یا compose test environment بنویسید.

## پذیرش و verification

- migration از database خالی اجرا شود و schema را بسازد.
- تست نشان دهد دو Candidate با URL canonical یکسان ثبت نمی‌شوند.
- تست نشان دهد AuditEvent و ScriptVersion قبلی با update version جدید حذف نمی‌شوند.
- build و test commandهای Plan 001 سبز باشند.

## STOP conditions

- اگر ORM یا naming convention از template متفاوت است، همان convention را دنبال کنید و تفاوت را در handback ثبت کنید.
- اگر JSON columns برای facts/uncertainty پشتیبانی نمی‌شوند، قبل از طراحی جایگزین stop کنید.

## تحویل به AI بعدی

نام aggregateها، migration name و ruleهای database را در README فنی یا این plan ثبت کنید.

## وضعیت اجرا

DONE. نام‌ها از `Shorts.*` به `CreatorRizz.*` تغییر کرد.

**نام aggregateها:** `TopicCandidate` (با `SourceItem` و `ResearchPack`)، `Production` (با `ScriptVersion`، `Asset` از طریق `AssetUsage` و `ReviewDecision`)، و `AuditEvent`.

**Migration:** `20260926092711_InitialSchema` در `src/CreatorRizz.Infrastructure/Persistence/Migrations`. EF Core تنها مالک schema است و `db/init/001_schema.sql` حذف شد تا دو منبع schema با هم drift نکنند.

**Ruleهای database که در مدل EF تعریف شده‌اند:**

- `topic_candidates.canonical_url` یکتا (`IX_topic_candidates_canonical_url`).
- `script_versions` روی `(production_id, version)` یکتا.
- `assets.checksum` یکتا؛ چند `NULL` مجاز است.
- `source_items` دارای `CHECK (reliability_score BETWEEN 0 AND 100)`.
- `asset_usages`، `source_items`، `research_packs`، `script_versions` و `review_decisions` دارای foreign key به والد؛ `AssetUsage` بدون `Production` یا `Asset` در دیتابیس ممکن نیست.
- `productions`، `script_versions` و `review_decisions` دارای `ON DELETE RESTRICT` تا تاریخچه با حذف والد پاک نشود.
- `audit_events.id` از نوع `bigint identity`؛ رکورد audit فقط درج می‌شود، هرگز به‌روزرسانی یا حذف نمی‌شود.
- همهٔ timestampها `timestamptZ` هستند و از `DateTimeOffset.UtcNow` می‌آیند.

**تصمیم معماری که در این plan ثبت شد:** `asset_usages.narrative_purpose` به پورت `IProductionRepository.AttachAsset` اضافه شد، چون این ستون `NOT NULL` بود و هیچ مسیری آن را پر نمی‌کرد. `in_milliseconds` و `out_milliseconds` nullable شدند چون زمان‌بندی منبع فقط با render manifest معلوم می‌شود؛ `BeginRendering` آن‌ها را از `TimelineClip` پر می‌کند.

**تست:** `tests/CreatorRizz.Persistence.Tests` در هر اجرا یک دیتابیس موقت روی PostgreSQL واقعی می‌سازد، migration را روی آن اجرا می‌کند و دیتابیس را حذف می‌کند. پوشش: ساخت schema روی دیتابیس خالی، رد canonical URL تکراری، حفظ script version و audit trail قدیمی، FK جدول `asset_usages`، باطل شدن حقوق با asset جدید، و زمان‌بندی که تا manifest نامعلوم می‌ماند.
