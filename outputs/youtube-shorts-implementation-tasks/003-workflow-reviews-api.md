# Plan 003: state machine و API گیت‌های انسانی

## وابستگی

Plan 002 باید DONE باشد.

## هدف

Production باید فقط با transitionهای مجاز حرکت کند. تایید Script، Rights و Publish با actor، زمان و یادداشت ثبت می‌شود و هر تغییر مهم بعد از حقوق تاییدشده، آن تایید را باطل می‌کند.

## Scope

- state machine در `Shorts.Domain`
- command/query endpointهای production و review در `Shorts.Api`
- authorization policy برای Operator و Reviewer
- unit و integration test

## وضعیت‌های مورد نیاز

Discovered، Scored، Researching، ResearchReady، ScriptDraft، ScriptInReview، ScriptApproved، AssetsPreparing، AssetsReady، RightsReview، RightsApproved، Rendering، Rendered، PublishReview، PublishApproved، Uploading، Published، Rejected و Failed.

## مراحل

1. transition table را در یک service یا aggregate method مرکزی پیاده کنید؛ controller نباید ruleهای transition را تکرار کند.
2. commandهای create production، submit script review، submit rights review، submit publish review، reject و rework را اضافه کنید.
3. optimistic concurrency را با version/ETag اجرا کنید تا دو بازبین همدیگر را overwrite نکنند.
4. هر decision و state change باید AuditEvent بسازد.

## پذیرش و verification

- AssetUsage تغییر یافته بعد از RightsApproved، production را به AssetsReady برمی‌گرداند و rights decision فعال را invalid می‌کند.
- کاربر Operator نمی‌تواند Rights Approval بدهد مگر role او اجازه داشته باشد.
- transition غیرمجاز API response مشخص و قابل فهم می‌دهد.
- integration testها concurrency conflict را پوشش می‌دهند.

## STOP conditions

- اگر نقش‌ها با auth provider انتخاب‌شده ناسازگارند، provider را عوض نکنید؛ مسئله را handback کنید.

## تحویل به AI بعدی

یک endpoint list مختصر و نمونه response خطای transition را به README اضافه کنید.

## وضعیت اجرا

DONE.

**transition table** در `ProductionWorkflow` به‌صورت توابع خالص روی `ProductionState` است. endpointها هیچ ruleی را تکرار نمی‌کنند؛ هر command فقط use case را صدا می‌زند و use case هم `IProductionRepository` را.

**سه transition مفقود که در عمل مسیر را غیرقابل پیمایش می‌کردند اضافه شدند:** `ScriptApproved → AssetsPreparing`، `AssetsPreparing → AssetsReady` و `Rendering → Rendered`. بدون آن‌ها `PublishApproved` که کل invariant این plan رویش نوشته شده بود غیرقابل تولید بود.

**optimistic concurrency** با `If-Match`/`ETag`:

- `GET /productions/{id}` نسخه را در `ETag: "7"` برمی‌گرداند.
- هر `POST` روی production موجود به `If-Match` نیاز دارد. غایب یا نامعتبر: `428 Precondition Required`. نسخهٔ قدیمی: `409 Conflict` با `currentVersion`.
- الزام در `ProductionVersionPreconditionMiddleware` است، نه در ۹ endpoint جداگانه.
- `expectedVersion` در تمام متدهای mutation پورت اجباری است و `Version` در EF Core concurrency token است، پس guard دامنه و داوری دیتابیس هر دو وجود دارند.
- `ProductionVersionConflict` جدا از `WorkflowRuleViolation` است چون به کلاینت می‌گوید دوباره بخواند، نه اینکه این کار هرگز مجاز نیست.

**نقش‌ها:** `X-Role: Reviewer` برای endpoint تصمیم بازبینی لازم است و در نبود آن `403` برمی‌گردد. این یک بررسی هدر است، نه احراز هویت واقعی؛ auth provider در Plan 008 انتخاب می‌شود. کسی که بتواند هدر را جعل کند می‌تواند خود را Reviewer معرفی کند، پس این بررسی قبل از آن فقط یک قرارداد API است، نه یک کنترل امنیتی.

**تست:** ۲۵ تست دامنه و ۱۲ تست integration روی PostgreSQL واقعی. دو تست integration جدا برای concurrency: یکی کلاینت ازپس‌مانده که قبل از هر نوشتنی رد می‌شود، و یکی دو writer که هر دو یک نسخه را خوانده‌اند و فقط یکی برنده می‌شود. مسیر کامل تا `PublishApproved` با ۲۳ بررسی روی HTTP واقعی تأیید شد، از جمله اینکه درخواست‌های رد‌شده هیچ رد پا و audit event باقی نمی‌گذارند.
