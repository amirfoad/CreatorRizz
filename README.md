# CreatorRizz

CreatorRizz ابزار داخلی برای کشف سوژه، تولید روایت، بازبینی حقوق، رندر و انتشار کنترل‌شده YouTube Shorts است.

لوگوی برنامه در `web/public/branding/creatorrizz-logo.png` قرار دارد و رابط وب از همان فایل استفاده می‌کند.

پالت رنگ و راهنمای استفاده در [Brand System](docs/brand/README.md) و فایل PDF مرجع آن قرار دارد.

وضعیت اجرایی taskها در [Task Progress](docs/task-progress.md) ثبت می‌شود.

قواعد توسعه و معماری backend در [AGENTS.md](AGENTS.md) قرار دارد.

نتیجه بازبینی معماری در [Architecture Review](docs/architecture-review.md) ثبت می‌شود.

راهنمای ساختار و مسئولیت‌های لایه Domain در [Domain Design](docs/domain-design.md) قرار دارد.

## ساختار

- `src/CreatorRizz.Api`: HTTP API، validation مرز HTTP و health endpoint
- `src/CreatorRizz.Domain`: مدل و ruleهای دامنه؛ شامل `Models/`، `Policies/`، `Workflow/`، `Rendering/`، `Captions/`، `Scoring/` و `Scripting/`
- `src/CreatorRizz.Application`: use caseها و portهای موردنیاز برای اجرای workflow
- `src/CreatorRizz.Infrastructure`: configuration، adapterهای بیرونی، EF Core و migrationهای PostgreSQL (`Persistence/`)
- `src/CreatorRizz.Workers`: workerهای پس زمینه
- `tests/CreatorRizz.Domain.Tests`: تست‌های دامنه و قرارداد adapterها
- `tests/CreatorRizz.Persistence.Tests`: تست‌های integration روی PostgreSQL واقعی
- `web`: React operations dashboard

## توسعه محلی

1. یک کپی از `.env.example` با نام `.env` بسازید و passwordهای محلی را تغییر دهید.
2. `docker compose up -d`
3. `dotnet restore CreatorRizz.sln`
4. `dotnet build CreatorRizz.sln --configuration Release`
5. `dotnet test CreatorRizz.sln --configuration Release`
6. از پوشه `web`: `npm ci`، سپس `npm run typecheck` و `npm run build`

API در development با `dotnet run --project src/CreatorRizz.Api` اجرا می‌شود. `GET /health/live` زنده‌بودن پردازش و `GET /health/ready` آمادگی تنظیمات لازم را نشان می‌دهد.

در محیط `Development`، Swagger UI از مسیر `/swagger` و سند OpenAPI از `/swagger/v1/swagger.json` در دسترس است. این UI در محیط‌های غیرdevelopment فعال نمی‌شود.

## PostgreSQL و schema

EF Core تنها مالک schema است. migrationها در `src/CreatorRizz.Infrastructure/Persistence/Migrations` قرار دارند و API پیش از پذیرش ترافیک آن‌ها را اجرا می‌کند؛ شکست migration عمداً startup را متوقف می‌کند. فایل SQL دستی وجود ندارد تا schema دو منبع نداشته باشد.

برای ساخت migration تازه:

```powershell
dotnet ef migrations add <Name> --project src/CreatorRizz.Infrastructure --output-dir Persistence\Migrations
```

`CreatorRizzDesignTimeDbContextFactory` فقط برای همین دستور است و connection string آن هرگز به دیتابیس وصل نمی‌شود.

`PostgresCandidateRepository` و `PostgresProductionRepository` تنها adapterهای persistence هستند و در `ServiceCollectionExtensions` به‌عنوان scoped ثبت می‌شوند. ترجمهٔ خطاهای provider در `DbContextSaveExtensions` انجام می‌شود تا repositoryها exception خام Npgsql را افشا نکنند.

قیدهای database که در مدل EF تعریف شده‌اند: `canonical_url` یکتا، `(production_id, version)` یکتا برای script version، `reliability_score` بین ۰ تا ۱۰۰، و foreign key برای جلوگیری از `AssetUsage` بدون Production یا Asset. جدول‌های تاریخی (`productions`، `script_versions`، `review_decisions`) `ON DELETE RESTRICT` دارند تا تاریخچه با حذف والد پاک نشود.

## مسیر کامل workflow

هر production از این مسیر عبور می‌کند و هیچ گیتی قابل دور زدن نیست:

`ScriptDraft` → submit/approve → `ScriptApproved` → `assets/prepare` → `AssetsPreparing` → `assets/ready` → `AssetsReady` → submit/approve → `RightsApproved` → `render` → `Rendering` → `render/complete` → `Rendered` → submit/approve → `PublishApproved`

هر transition یک command صریح در `CreatorRizzWorkflow`، یک متد روی `IProductionRepository` و یک endpoint دارد. زمان‌بندی منبع (`in_milliseconds` و `out_milliseconds`) تا دریافت render manifest نامعلوم می‌ماند و بعد از آن از `TimelineClip` پر می‌شود.

در JSON، وضعیت‌های lifecycle و حقوق فقط با نام خودشان پذیرفته می‌شوند (مثلاً `"Licensed"` و `"PublishApproved"`). ارسال عدد برای این enumها خطای 400 می‌دهد، چون شمارهٔ ناشناخته می‌توانست بی‌سروصدا permissive‌ترین وضعیت حقوق را درخواست کند.

## کنترل همزمانی روی Production

هر درخواست تغییردهنده باید نسخه‌ای را که خوانده اعلام کند، وگرنه دو بازبین روی یک گیت همدیگر را بی‌سروصدا پاک می‌کنند. `GET /productions/{id}` نسخه را در هدر `ETag` برمی‌گرداند و هر `POST` روی یک production موجود به هدر `If-Match` نیاز دارد:

```http
GET /productions/{id}
-> ETag: "7"

POST /productions/{id}/reviews/Script
If-Match: "7"
```

`POST /productions` استثناست چون production را می‌سازد و هنوز نسخه‌ای ندارد.

- هدر `If-Match` غایب یا نامعتبر: `428 Precondition Required` با راهنمایی برای خواندن production
- نسخهٔ قدیمی: `409 Conflict` با `currentVersion` تا کلاینت یک‌بار دیگر بخواند و خودش تصمیم بگیرد؛ `retry` کور کار نمی‌کند
- دو درخواستی که هر دو یک نسخه را خوانده‌اند و هم‌زمان می‌نویسند: `Version` در EF Core concurrency token است و دیتابیس فقط یکی را برنده اعلام می‌کند

`GET /productions/{id}` بعد از هر تغییر نسخهٔ جدید را برمی‌گرداند. endpointهای تغییردهنده نسخهٔ جدید را در پاسخ نمی‌دهند تا هیچ جایی فرض نکند نسخه‌ها دقیقاً یکی زیاد می‌شوند.

Discovery اولیه RSS در `src/CreatorRizz.Infrastructure/Discovery/RssDiscovery.cs` قرار دارد. قبل از اتصال feedهای واقعی، آن‌ها باید در allowlist عملیاتی پروژه ثبت شوند. `RssDiscoverySource` هر feed را فقط پس از اعتبارسنجی allowlist خواند.

### Plan 004: discovery، research pack و provenance

- هر Candidate با `fingerprint` نرمال‌شدهٔ SHA-256 عنوان و creator شناسایی می‌شود، نه فقط URL؛ همان داستان در دو feed با URL و نگارش متفاوت یکی محسوب می‌شود و repeat هیچ خطایی نمی‌دود بلکه candidate قبلی را برمی‌گرداند.
- `ViralScoreWeights` باید مجموع دقیقاً ۱۰۰ داشته باشد. هر نام ناشناختهٔ weight در config زمان startup صدا می‌زند تا اشتباه تایپی مثل `Engagement` به‌جای `ViewVelocity` همهٔ scoreها را خاموز نکند.
- `IScriptGenerator` پورت است. `DisabledScriptGenerator` بدون مدل و بدون API key validate می‌کند و خطای قابل‌اقدام می‌دهد؛ هرگز نمی‌کوشد template را به‌صورت uncited AI تقلب کند. `ScriptDraftComposer` حذف شد چون دلیلش ناپدید شده بود.
- `ResearchPolicy` حداقل دو منبع با `ReliabilityScore >= 50` و excerpt می‌طلبد. یک پژوهش با منبع واحد هرگز pack نمی‌شود.
- `ScriptGeneration` با `ScriptVersion` در یک write ثبت می‌شود، پس `AddGeneratedScript` هیچ اثر جانبی ندارد، اما `SaveVersion` و `Save` را اجرا می‌کند تا `GetScripts` همواره واقعاً موجود باشد.
- یک `DiscoveryWorker` timer-based هر feed allowlisted را بازبینی می‌کند. یک `ResearchWorker` هر Candidate را بررسی می‌کند و وقتی pack آماده باشد آن را می‌سازد، بدون اینکه Candidateهای ناقص را مجبور به پردازش کند.
- هر `POST` تغییردهنده production باید `If-Match` داشته باشد، حتی وقتی script از research draft می‌شود. بدون آن، ۴۲۸ و بدون آن، ۴۰۹.
- در API، `ProductionEndpoints` حالا `GET /productions/{id}/scripts` و `GET /productions/{id}/script-generations` را هم برمی‌گرداند تا provenance قابل بازبینی باشد.

در API، `Program.cs` فقط composition root است. قراردادهای HTTP در `Contracts/`، endpointهای هر جریان در `Endpoints/`، middlewareها در `Middleware/` و ترجمهٔ خطاهای موردانتظار در `Results/` نگه‌داری می‌شوند.

وابستگی backend به سمت داخل است: `Domain ← Application ← Infrastructure/API`. API فقط درخواست HTTP را به use caseهای Application می‌سپارد و Infrastructure، portهای Application را پیاده‌سازی می‌کند.

Infrastructure بر اساس نوع adapter دسته‌بندی شده است: `Configuration/`، `DependencyInjection/`، `Persistence/`، `Jobs/`، `Storage/`، `Discovery/`، `Tts/` و `Publishing/`.

در Visual Studio، تمام لایه‌های runtime زیر solution folderِ `src` و پروژه‌های test زیر `tests` نمایش داده می‌شوند.

## تست integration

`CreatorRizz.Persistence.Tests` در هر اجرا یک دیتابیس موقت روی PostgreSQL واقعی می‌سازد، migrationها را روی آن اجرا می‌کند و در پایان آن را حذف می‌کند. یعنی تست هرگز skip نمی‌شود؛ اگر دیتابیس در دسترس نباشد با خطا شکست می‌خورد.

پیش‌فرض به سرویس محلی Docker Compose اشاره می‌کند. برای سرویس دیگر:

```powershell
$env:CREATORRIZZ_TEST_POSTGRES = "Host=localhost;Port=5432;Database=shorts;Username=shorts;Password=shorts"
dotnet test CreatorRizz.sln --configuration Release
```
