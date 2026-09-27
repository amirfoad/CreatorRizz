# CreatorRizz

CreatorRizz ابزار داخلی برای کشف سوژه، نوشتن روایت، بازبینی حقوق و رندر کنترل‌شده YouTube Shorts است.

بخش‌هایی که هنوز به خروجی واقعی نمی‌رسند: رندر (worker و ساخت دستور FFmpeg اضافه شده‌اند، ولی FFmpeg روی این ماشین نصب نیست و هیچ encode واقعی اجرا نشده)، TTS (provider ثبت‌شده خطا می‌دهد و هیچ job نوع `TextToSpeech` مصرف نمی‌شود) و انتشار به YouTube (`IYouTubePublisher` هنوز به هیچ endpoint یا transitionی وصل نیست). وضعیت دقیق هر کدام در [Task Progress](docs/task-progress.md) و فهرست کار باقی‌مانده در [Architecture Review](docs/architecture-review.md) آمده است.

لوگوی برنامه در `web/public/branding/creatorrizz-logo.png` قرار دارد و رابط وب از همان فایل استفاده می‌کند.

پالت رنگ و راهنمای استفاده در [Brand System](docs/brand/README.md) و فایل PDF مرجع آن قرار دارد.

وضعیت اجرایی taskها در [Task Progress](docs/task-progress.md) ثبت می‌شود.

تحلیل تازهٔ vidIQ و مسیر تبدیل پروژه به محصول اشتراکی بین‌المللی در [Product Roadmap](.agents/ROADMAP.md) ثبت شده است. هدف، رابط فارسی و انگلیسی، YouTube برای همه و نمایش آپارات برای بازار ایران است. این نقشه‌راه پیشنهاد توسعه است؛ قابلیت‌های دوزبانه، اتصال آپارات، تحلیل کانال و فروش اشتراک هنوز پیاده‌سازی نشده‌اند.

قواعد توسعه و معماری backend در [AGENTS.md](AGENTS.md) قرار دارد.

نتیجه بازبینی معماری در [Architecture Review](docs/architecture-review.md) ثبت می‌شود.

راهنمای ساختار و مسئولیت‌های لایه Domain در [Domain Design](docs/domain-design.md) قرار دارد.

## ساختار

- `src/CreatorRizz.Api`: HTTP API، validation مرز HTTP، احراز هویت و health endpoint
- `src/CreatorRizz.Domain`: مدل و ruleهای دامنه؛ شامل `Models/`، `Policies/`، `Workflow/`، `Rendering/`، `Captions/`، `Scoring/` و `Scripting/`
- `src/CreatorRizz.Application`: use caseها و portهای موردنیاز برای اجرای workflow
- `src/CreatorRizz.Infrastructure`: configuration، adapterهای بیرونی، EF Core و migrationهای PostgreSQL (`Persistence/`)
- `src/CreatorRizz.Workers`: workerهای پس زمینه
- `tests/CreatorRizz.Domain.Tests`: تست‌های دامنه و قرارداد adapterها
- `tests/CreatorRizz.Persistence.Tests`: تست‌های integration روی PostgreSQL واقعی
- `web`: رابط React. در حال حاضر فقط یک صفحهٔ برند است که API را صدا نمی‌زند؛ داشبورد عملیاتی هنوز ساخته نشده است.

## توسعه محلی

1. یک کپی از `.env.example` با نام `.env` بسازید و passwordهای محلی را تغییر دهید.
2. `docker compose up -d`
3. `dotnet restore CreatorRizz.sln`
4. `dotnet build CreatorRizz.sln --configuration Release`
5. `dotnet test CreatorRizz.sln --configuration Release`
6. از پوشه `web`: `npm ci`، سپس `npm run typecheck` و `npm run build`
7. `dotnet user-jwts create --project src/CreatorRizz.Api --role reviewer` تا یک توکن بازبین برای کار محلی داشته باشید. بدون یک منبع توکن، API عمداً startup نمی‌شود.

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

قیدهای database که در مدل EF تعریف شده‌اند: `canonical_url` یکتا، `(production_id, version)` یکتا برای script version، `idempotency_key` یکتا در `job_outbox`، `reliability_score` بین ۰ تا ۱۰۰، و foreign key برای جلوگیری از `AssetUsage` بدون Production یا Asset. جدول‌های تاریخی (`productions`، `script_versions`، `review_decisions`) `ON DELETE RESTRICT` دارند تا تاریخچه با حذف والد پاک نشود.

## ثبت کار و انجام اتمیک آن

هر کاری که یک تغییر وضعیت ایجاب می‌کند، همان لحظه در جدول `job_outbox` ثبت می‌شود و هر دو در یک transaction می‌نویسند. `QueueRenderAsync` وضعیت را به `Rendering` می‌برد و ردیف کار را در همان تراکنش ثبت می‌کند، پس قطع ارتباط بین این دو دیگر production را در انتظار کاری که هرگز ثبت نشده رها نمی‌کند.

- `IWorkflowTransaction` پورتی در Application است و `PostgresWorkflowTransaction` پیاده‌سازی آن در Infrastructure. هر دو روی همان `CreatorRizzDbContext` scope اجرا می‌شوند، که در `ServiceCollectionExtensions` به‌صورت scoped ثبت شده‌اند.
- کلید idempotency از نسخه‌ای ساخته می‌شود که درخواست‌دهنده خوانده، نه از نسخهٔ بعدیِ حدس‌زده‌شده. enqueue تکراری همان کلید یک کار است نه دو، و ایندکس یکتای database مرجع نهایی است.
- **مصرف صف با lease انجام می‌شود، نه با حذف ردیف.** ستون‌های `claimed_at`, `claimed_by`, `completed_at`, `attempts` و `last_error` روی `job_outbox` هستند. کاری که workerش می‌میرد حذف نمی‌شود؛ lease منقضی می‌شود و ردیف دوباره پیشنهاد می‌شود، و دلیل شکست روی ردیف می‌ماند.
- `IProductionJobDispatcher` پورت این کار در Application و `PostgresProductionJobDispatcher` پیاده‌سازی آن در Infrastructure است. claim یک `UPDATE` با زیرپرسمان `FOR UPDATE SKIP LOCKED` است: دو worker هرگز یک ردیف را نمی‌گیرند و بازندهٔ مسابقه پشت قفل منتظر نمی‌ماند بلکه ردیف بعدی را برمی‌دارد.
- `RenderJobWorker` در `CreatorRizz.Workers` فقط job نوع `Render` را claim می‌کند، خروجی FFmpeg را در object storage می‌نویسد، `CompleteRendering` را صدا می‌زند و ردیف را کامل می‌کند. خطا ردیف را آزاد می‌کند تا با سقف `MaxAttempts` دوباره تلاش شود. نسخهٔ production را از دیتابیس می‌خواند و حدس نمی‌زند، تا قاعدهٔ If-Match دور زده نشود.
- job نوع `TextToSpeech` عمداً مصرف نمی‌شود؛ provider واقعی و محل ثبت خروجی صوتی وجود ندارد.

## خواندن backlog و صف بازبینی

`GET /productions` صفحه‌ای از productionها می‌دهد. بدون فیلتر، کل backlog است:

```http
GET /productions?state=ScriptDraft&skip=0&take=50
```

با `awaitingReview` همان endpoint صف بازبینی می‌شود — یعنی فقط productionهایی که منتظر یک تصمیم هستند:

```http
GET /productions?awaitingReview=Script
GET /productions?awaitingReview=Rights
GET /productions?awaitingReview=Publish
```

- این یک endpoint است نه دو، چون هر دو از یک جدول و یک شرط می‌آیند. نگاشت state به نوع بازبینی در `ProductionWorkflow.ReviewAwaitingDecision` زندگی می‌کند، کنار همان transitionهایی که آن state را می‌سازند.
- پاسخ `Items`، `TotalCount`، `Skip` و `HasMore` دارد. `TotalCount` شمارش کل فیلتر است نه شمارش صفحه، وگرنه کلاینت نمی‌تواند صفحه‌بندی کند.
- هر ردیف `State` و `Version` خودش را دارد. نسخه را باید خواند و در `If-Match` فرستاد؛ حدس زدنش یعنی گرفتن conflict به‌جای بازنویسی کار بازبین دیگر.
- `take` بین ۱ و ۲۰۰ است و مقدار بیرون از آن ۴۰۰ می‌دهد، همان‌طور که یک `state` یا `awaitingReview` ناشناخته ۴۰۰ می‌دهد. این‌ها ۴۰۰ هستند نه ۴۰۴: درخواست درست است ولی دربارهٔ چیزی می‌پرسد که این سرویس ندارد.

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

Discovery اولیه RSS در `src/CreatorRizz.Infrastructure/Discovery/RssDiscovery.cs` قرار دارد. قبل از اتصال feedهای واقعی، آن‌ها باید در allowlist عملیاتی پروژه ثبت شوند. `RssDiscoverySource` هر feed را فقط پس از اعتبارسنجی allowlist می‌خواند و redirect‌ها را دستی دنبال می‌کند تا هر hop جداگانه بررسی شود؛ `HttpClient` مربوطه با `AllowAutoRedirect = false` ساخته می‌شود، چون در غیر این صورت میزبان نهایی پیش از دیده‌شدن توسط این کلاس خوانده می‌شود. میزبانی که در `AllowedHosts` نباشد، حتی از راه redirect، دریافت نمی‌شود.

### Plan 004: discovery، research pack و provenance

- هر Candidate با `fingerprint` نرمال‌شدهٔ SHA-256 عنوان و creator شناسایی می‌شود، نه فقط URL؛ همان داستان در دو feed با URL و نگارش متفاوت یکی محسوب می‌شود و repeat هیچ خطایی نمی‌دود بلکه candidate قبلی را برمی‌گرداند.
- `ViralScoreWeights` باید مجموع دقیقاً ۱۰۰ داشته باشد. هر نام ناشناختهٔ weight در config زمان startup صدا می‌زند تا اشتباه تایپی مثل `Engagement` به‌جای `ViewVelocity` همهٔ scoreها را خاموز نکند.
- `IScriptGenerator` پورت است. `OpenAiCompatibleScriptGenerator` هر endpoint سازگار با شکل OpenAI chat completions را می‌خواند. `DisabledScriptGenerator` وقتی هیچ credentialای پیکربندی نشده ثبت می‌شود، validate می‌کند و خطای قابل‌اقدام می‌دهد؛ هرگز نمی‌کوشد template را به‌صورت uncited AI تقلب کند. `ScriptDraftComposer` حذف شد چون دلیلش ناپدید شده بود.
- `ResearchPolicy` حداقل دو منبع با `ReliabilityScore >= 50` و excerpt می‌طلبد. یک پژوهش با منبع واحد هرگز pack نمی‌شود.
- `ScriptGeneration` با `ScriptVersion` در یک write ثبت می‌شود، پس `AddGeneratedScript` هیچ اثر جانبی ندارد، اما `SaveVersion` و `Save` را اجرا می‌کند تا `GetScripts` همواره واقعاً موجود باشد.
- یک `DiscoveryWorker` timer-based هر feed allowlisted را بازبینی می‌کند. یک `ResearchWorker` هر Candidate را بررسی می‌کند و وقتی pack آماده باشد آن را می‌سازد، بدون اینکه Candidateهای ناقص را مجبور به پردازش کند.
- هر `POST` تغییردهنده production باید `If-Match` داشته باشد، حتی وقتی script از research draft می‌شود. بدون آن، ۴۲۸ و بدون آن، ۴۰۹.
- در API، `ProductionEndpoints` حالا `GET /productions/{id}/scripts` و `GET /productions/{id}/script-generations` را هم برمی‌گرداند تا provenance قابل بازبینی باشد.

در API، `Program.cs` فقط composition root است. قراردادهای HTTP در `Contracts/`، endpointهای هر جریان در `Endpoints/`، middlewareها در `Middleware/` و ترجمهٔ خطاهای موردانتظار در `Results/` نگه‌داری می‌شوند.

## ساخت اسکریپت با مدل

`OpenAiCompatibleScriptGenerator` تنها adapterای است که به یک مدل بیرونی حرف می‌زند و با هر سرویس سازگار با شکل OpenAI chat completions کار می‌کند؛ مدل میزبانی‌شده یا محلی، با یک کد.

پیکربندی در بخش `ScriptGenerator`:

```json
{
  "ScriptGenerator": {
    "BaseUrl": "https://your-endpoint/v1",
    "ApiKey": "",
    "Model": "your-model",
    "PromptVersion": "script-v1",
    "TimeoutSeconds": 120
  }
}
```

- `BaseUrl` باید https باشد، مگر روی loopback که برای مدل محلی لازم است. کلید به‌صورت bearer token فرستاده می‌شود و redirect دنبال نمی‌شود تا کلید به میزبانی که اپراتور نام نبرده دوباره فرستاده نشود.
- اگر `BaseUrl`، `ApiKey` یا `Model` خالی باشد، `DisabledScriptGenerator` ثبت می‌شود. یعنی نبود کلید باعث نمی‌شود سرویس بالا نیاید؛ خطا در همان گیت draft و با پیام قابل‌اقدام داده می‌شود.
- فقط منبع‌هایی به مدل داده می‌شوند که `ResearchPolicy` پشتشان می‌ایستد: `ReliabilityScore >= 50` و دارای excerpt.

**قاعده‌ای که این adapter نگه می‌دارد:** مدل یک‌بار اسکریپت و claim map را برمی‌گرداند و claim map پیش از ورود به گیت بازبینی اسکریپت در برابر منبع‌های داده‌شده بررسی می‌شود. هر claim که `sourceId` ناشناخته داشته باشد کل پاسخ دور ریخته می‌شود، نه فقط آن claim. دلیلش این است که یک citation ساختگی یعنی متنی که بازبین نمی‌تواند راستی‌آزمایی کند، و همان چیزی است که این workflow برای جلوگیری از آن ساخته شده. هزینهٔ این تصمیم، retry است.

خطای provider عمداً از `WorkflowRuleViolation` جدا است. `ScriptGenerationFailedException` یعنی درخواست سالم بوده و provider جواب مفیدی نداده، پس endpoint آن را **۵۰۲** می‌دهد نه ۴۰۹. کلاینتی که ۴۰۹ می‌بیند دست می‌کشد؛ کلاینتی که ۵۰۲ می‌بیند دوباره تلاش می‌کند.

وابستگی backend به سمت داخل است: `Domain ← Application ← Infrastructure/API`. API فقط درخواست HTTP را به use caseهای Application می‌سپارد و Infrastructure، portهای Application را پیاده‌سازی می‌کند.

Infrastructure بر اساس نوع adapter دسته‌بندی شده است: `Configuration/`، `DependencyInjection/`، `Persistence/`، `Jobs/`، `Storage/`، `Discovery/`، `Tts/` و `Publishing/`.

در Visual Studio، تمام لایه‌های runtime زیر solution folderِ `src` و پروژه‌های test زیر `tests` نمایش داده می‌شوند.

## احراز هویت و نقش بازبین

هیچ endpointای بدون توکن معتبر باز نیست، و نقش بازبین دیگر از هدر `X-Role` یا بدنهٔ درخواست خوانده نمی‌شود. هر دو از ادعاهای توکن می‌آیند.

- **توکن بیرونی است.** API توکن صادر نمی‌کند و هیچ signing secret خودش ندارد. `ApiAuthentication` فقط اعتبارسنجی می‌کند.
- **`FallbackPolicy` روی همهٔ endpointها اعمال می‌شود** و فقط `RequireAuthenticatedUser` می‌خواهد. یعنی endpoint تازه پیش‌فرض بسته است و فقط وقتی کسی آگاهانه `AllowAnonymous` بگذارد باز می‌شود. `/health/live` و `/health/ready` تنها استثنا هستند.
- **عبور از گیت تصمیم نقش می‌خواهد.** `POST /productions/{id}/reviews/{kind}` به سیاست `reviewer` نیاز دارد که `RequireAuthenticatedUser`، نقش `reviewer` و وجود claim `sub` را با هم می‌خواهد.
- **نام بازبین از claim `sub` خوانده می‌شود** (`ReviewerAccess.ReadId`) و روی review decision و audit event ثبت می‌شود. `ReviewRequest` فیلد `ReviewerId` ندارد، پس caller نمی‌تواند با ویرایش یک فیلد، تصمیم را به نام دیگری ثبت کند. توکنی که `sub` نداشته باشد خطا می‌دهد و قابل نسبت‌دادن نیست.
- **`MapInboundClaims = false`** چون نگاشت پیش‌فرض SDK نام `sub` و نقش‌ها را به URIهای طولانی claim بازنویسی می‌کند و سیاست reviewer معنای دیگری پیدا می‌کند.
- **startup بدون منبع توکن شکست می‌خورد.** `Audience` و وجود یکی از این دو باید پیکربندی شده باشد، وگرنه `ValidateOnStart` قبل از پذیرش ترافیک می‌ایستد: یک `Authority` (ارائه‌دهندهٔ هویت) یا کلیدهای امضا زیر `TokenValidationParameters:IssuerSigningKeys`. سرویسی که نتواند توکر واقعی را از جعلی تشخیص دهد نباید بالا بیاید.
- **`ProductionVersionPreconditionMiddleware` بعد از `UseAuthentication` اجرا می‌شود.** بدون توکن، پاسخ ۴۰۱ است نه ۴۲۸؛ گیت نسخه راهی برای کاوش endpointی که اجازهٔ فراخوانی‌اش را نداری نیست.

پیکربندی در بخش `Authentication` است:

```json
{
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "Authority": "https://login.example.com",
        "Audience": "creatorrizz-api",
        "RequireHttpsMetadata": true
      }
    }
  }
}
```

برای توسعهٔ محلی بدون ارائه‌دهندهٔ هویت، کلید ابزار رسمی SDK را می‌خواند:

```powershell
dotnet user-jwts create --project src/CreatorRizz.Api --role reviewer
```

این فرمان کلیدها را در user secrets زیر `Authentication:Schemes:Bearer:SigningKeys` می‌نویسد. `LocalSigningKeysFromUserJwts` آن‌ها را روی `IssuerSigningKeys` نگاشت می‌کند، چون خود JWT handler این بخش را نمی‌خواند؛ بدون این نگاشت ابزار توکن می‌دهد، سرویس بالا می‌آید و بعد همان توکن را رد می‌کند. این نگاشت فقط وقتی `Authority` خالی است اعمال می‌شود، پس استقرار با ارائه‌دهندهٔ واقعی دست‌نخورده می‌ماند.

Swagger در محیط توسعه دکمهٔ Authorize دارد و درخواست‌ها را با همان توکن می‌فرستد.

## محدودیت‌های شناخته‌شده

این‌ها نقص پنهان نیستند و در فهرست بالا هم آمده‌اند، ولی استفاده از API بدون دانستن آن‌ها گمراه‌کننده است.

- **مدل دسترسی فقط «توکن معتبر» و «نقش reviewer» است.** هر کسی که یک توکن معتبر داشته باشد می‌تواند همهٔ endpointهای غیر‌health را صدا بزند و فقط گذراندن گیت تصمیم به نقش `reviewer` نیاز دارد. جداسازی مشتری، workspace و مالکیت production هنوز در مدل وجود ندارد، پس این برای فروش عمومی کافی نیست.
- **صف job فقط برای Render مصرف می‌شود.** `RenderJobWorker` ردیف‌های نوع `Render` را claim و اجرا می‌کند، ولی FFmpeg روی این ماشین نصب نیست و provider واقعی TTS هم وجود ندارد. ردیف‌های نوع `TextToSpeech` ثبت می‌شوند و هرگز برداشته نمی‌شوند، و هیچ encode واقعی اجرا نشده است.
- **`RedisConnectionString` و `ObjectStorageEndpoint` استفاده نمی‌شوند.** هر دو در startup اعتبارسنجی می‌شوند و بعد رها می‌شوند. تنها storage فعال، `LocalObjectStorage` روی دیسک است و سرویس MinIO در `docker-compose.yml` هیچ clientای ندارد.
- **`GET /productions` تازه اضافه شده است** ولی رابط وب هنوز آن را صدا نمی‌زند. `web` فقط یک صفحهٔ برند است، پس صف بازبینی هنوز از API خوانده می‌شود نه از داشبورد.
- **rate limiter یک bucket مشترک دارد** با کلید ثابت، پس کل ترافیک از جمله `/health/*` در یک سهمیهٔ ۶۰ در دقیقه جمع می‌شود.

## تست integration

`CreatorRizz.Persistence.Tests` در هر اجرا یک دیتابیس موقت روی PostgreSQL واقعی می‌سازد، migrationها را روی آن اجرا می‌کند و در پایان آن را حذف می‌کند. یعنی تست هرگز skip نمی‌شود؛ اگر دیتابیس در دسترس نباشد با خطا شکست می‌خورد.

پیش‌فرض به سرویس محلی Docker Compose اشاره می‌کند. برای سرویس دیگر:

```powershell
$env:CREATORRIZZ_TEST_POSTGRES = "Host=localhost;Port=5432;Database=shorts;Username=shorts;Password=shorts"
dotnet test CreatorRizz.sln --configuration Release
```
