# Architecture Review

## 2026-09-26

### persistence روی PostgreSQL

- `PostgresProductionRepository` نوشته شد و `PostgresCandidateRepository` به DI متصل شد؛ هر دو به‌عنوان scoped ثبت می‌شوند چون `DbContext` scoped است.
- `InMemoryCandidateStore` و `InMemoryProductionStore` حذف شدند. دیگر مصرفی نداشتند و دو مسیر persistence برای یک invariant، یعنی گیت‌های بازبینی، وجود می‌آوردند.
- EF Core تنها مالک schema شد. `db/init/001_schema.sql` حذف و `InitialSchema` migration ساخته شد. دو منبع schema تضمیناً با هم drift می‌کردند.
- API پیش از پذیرش ترافیک migrationها را اجرا می‌کند و در صورت شکست startup متوقف می‌شود، چون ادامهٔ کار با schema ناشناخته یعنی خطاهای مبهم در زمان اجرا.
- `CreatorRizzDesignTimeDbContextFactory` اضافه شد تا `dotnet ef` بدون اجرای API کار کند.
- `OnModelCreating` از حالت تک‌خطی خارج شد. نگاشت‌ها باید قابل بازبینی باشند و قیدهای اضافه‌شده در همین متد قرار می‌گیرند.
- قیدهای `foreign key` که در SQL دستی وجود داشتند اما در مدل EF نبودند به مدل اضافه شدند، به‌علاوهٔ `CHECK` برای `reliability_score` و ایندکس یکتای `checksum`.
- `productions`، `script_versions` و `review_decisions` `ON DELETE RESTRICT` گرفتند تا append-only بودن تاریخچه به یک accident وابسته نباشد.
- `asset_usages` در `DbContext` نگاشت شد؛ قبلاً جدول در schema وجود داشت اما در مدل EF نبود، پس `GetAssets` هیچ پیوندی به production نمی‌داشت.
- ترجمهٔ خطای provider در `DbContextSaveExtensions` متمرکز شد تا repositoryها exception خام Npgsql را به API leak نکنند.
- `asset_usages.narrative_purpose` به پورت `AttachAsset` و قرارداد HTTP اضافه شد. قبلاً این ستون `NOT NULL` بود ولی هیچ مسیری آن را پر نمی‌کرد.
- `in_milliseconds` و `out_milliseconds` nullable شدند، چون زمان‌بندی منبع تا دریافت render manifest معلوم نیست. `BeginRendering` آن‌ها را از `TimelineClip` پر می‌کند و `AssetUsagePolicy` همان قاعده را در دامنه نگه می‌دارد.
- `AssetUsagePolicy` به‌عنوان مالک قاعدهٔ purpose و ترتیب زمان‌بندی اضافه شد.

### نقص‌هایی که persistence آشکار کرد

- **گیت حقوق و publish غیرقابل دستیابی بودند.** `ScriptApproved` هیچ transitionی به `AssetsPreparing` نداشت، `AssetsPreparing` به `AssetsReady` برنمی‌گشت و `Rendering` به `Rendered` نمی‌رسید. یعنی `PublishApproved` که invariant اصلی رویش نوشته شده بود، در عمل غیرقابل تولید بود. سه transition با command و endpoint صریح اضافه شد.
- **`payload_json` از نوع jsonb بود ولی payload یک رشتهٔ سادهٔ غیر-JSON بود.** تست integration این را به‌صورت خطای PostgreSQL نشان داد. payload حالا با `JsonSerializer` کدگذاری می‌شود.
- **`facts_json` و `uncertainty_json` ورودی نامعتبر را به خطای PostgreSQL می‌سپردند.** حالا با پیام قابل فهم در مرز repository رد می‌شوند.
- **`canonicalUrl` نال باعث 500 می‌شد** چون `.Trim()` روی نال اجرا می‌شد. حالا ورودی خالی با 400 رد می‌شود.
- **enumها روی JSON عددی سفر می‌کردند.** کلاینتی که شماره‌ها را نمی‌شناخت می‌توانست `0` بفرستد و بی‌سروصدا `RightsStatus.Owned`، یعنی permissive‌ترین وضعیت، بگیرد. حالا فقط نام پذیرفته می‌شود.
- **connection string پیش‌فرض API با رمز compose هم‌خوان نبود**، پس مسیر مستند `docker compose up -d` هرگز وصل نمی‌شد.

### optimistic concurrency

- **دو بازبین می‌توانستند همدیگر را overwrite کنند.** `Production.Version` وجود داشت و در هر mutation یکی اضافه می‌شد، ولی هیچ‌جا از آن استفاده نمی‌شد؛ `Submit` و `Decide` وضعیت فعلی را از دیتابیس می‌خواندند و هرچه در دیتابیس بوده بود بازنویسی می‌کردند. یعنی تصمیم دوم بازبین، تصمیم اول را بی‌صدا پاک می‌کرد و audit trail دو تصمیم متضاد را نشان می‌داد.
- هر متد mutation در `IProductionRepository` حالا `expectedVersion` می‌گیرد. این عمداً همهٔ متدها را شامل می‌شود، نه فقط `Decide`: اگر فقط تصمیم بازبینی محافظت شود، همان باگ در `AttachAsset` و `BeginRendering` باقی می‌ماند و آن‌ها هم با TOCTOU روی state تصمیم می‌گیرند.
- `Production.EnsureVersion` تنها مالک این قاعده است و `ProductionVersionConflict` می‌سازد.
- `ProductionVersionConflict` عمداً از `WorkflowRuleViolation` ارث نمی‌برد. transition ممکن است بعد از خواندن نسخهٔ درست کاملاً مجاز باشد، پس پیامش «دوباره بخوان و خودت تصمیم بگیر» است، نه «این کار هرگز ممکن نیست».
- `Version` در EF Core concurrency token شد. guard دامنه کلاینت ازپس‌مانده را می‌گیرد، ولی حالتی که دو درخواست هر دو نسخهٔ یکسان را خوانده‌اند و سپس می‌نویسند را فقط دیتابیس می‌تواند داوری کند. هر دو مسیر تست جدا دارند.
- `DbUpdateConcurrencyException` زیرکلاس `DbUpdateException` است، پس catch عمومی آن را «PostgreSQL نتوانست تغییر را ذخیره کند» برچسب می‌زد. یک catch صریح آن را عبور می‌دهد تا repository بتواند نسخهٔ برنده را بخواند و گزارش دهد.
- الزام `If-Match` در middleware نگه‌داری می‌شود، نه در تک‌تک endpointها. با ۹ endpoint تغییردهنده، تکرار یک بررسی در هر کدام یعنی یکی از آنها فراموش می‌شود و باگ برمی‌گردد. دامنه هم جداگانه `expectedVersion` را اجباری می‌کند، پس فراموشی در یک لایه به باگ در لایهٔ دیگر تبدیل نمی‌شود.
- endpointهای تغییردهنده نسخهٔ جدید را در پاسخ نمی‌دهند. حساب `expectedVersion + 1` در middleware یعنی فرض پنهانی دربارهٔ اینکه هر mutation دقیقاً یکی زیاد می‌کند؛ این فرض در لایهٔ اشتباه و برای خواننده نامرئی است. کلاینت از `GET` نسخهٔ واقعی را می‌خواند.

### زیرساخت و تست

- `CreatorRizz.Persistence.Tests` اضافه شد. هر اجرا یک دیتابیس موقت می‌سازد، migrationها را روی سرور واقعی اجرا می‌کند و دیتابیس را حذف می‌کند. تست عمداً skip نمی‌شود.
- نام solution در `ci.yml` اشتباه بود (`ShortsAutomation.sln`) و آن workflow با هیچ تغییری شکست می‌خورد. اصلاح شد و سرویس PostgreSQL به آن اضافه شد.
- `package-lock.json` وجود نداشت، پس `npm ci` در CI کار نمی‌کرد. lockfile ساخته شد و `@types/react` و `@types/react-dom` که جا افتاده بودند اضافه شدند.
- `vite-env.d.ts` اضافه شد تا import فایل CSS در typecheck معتبر باشد.

### تصمیم‌هایی که باید ثبت شوند

- schema یک مالک دارد: EF Core migration. SQL دستی به‌عنوان منبع دوم حذف شد.
- `AttachAsset` علاوه بر `Asset` یک `narrativePurpose` می‌گیرد. این یک تصمیم معماری بود و در README و اسناد plan ثبت شده است.
- هر transition حالت، یک command صریح دارد. هیچ گیتی با فراخوانی مستقیم repository دور زده نمی‌شود.

### discovery و script provenance (Plan 004)

- `CandidateFingerprint` هر Candidate را از عنوان و creator نرمال‌شدهٔ SHA-256 شناسایی می‌کند، نه فقط URL؛ همان داستان در دو feed با URL و نگارش متفاوت یکی محسوب می‌شود. `TopicCandidate.Fingerprint` از نوع required است و در DB unique index دارد.
- `ViralScoreWeights` باید مجموع دقیقاً ۱۰۰ داشته باشد و در runtime هم validation شود. `ViralScore.Calculate` وزن‌ها را به عنوان پارامتر می‌گیرد تا یک signal را بتوان بدون اثر بر سایری‌ها آزمایش کرد.
- RSS فقط recency واقعی دارد؛ بقیهٔ signalها صفر می‌مانند، چون شاید امروز داشته باشید هیچ engagement واقعی نداشته باشید و هرگز score آن با Candidateهای دارای signal واقعی قابل مقایسه نباشد.
- `IDiscoverySource` پورت است و `DiscoveryFeedOptions.AllowedHosts` می‌گوید چه feedهایی اجازه خواندن دارند. هر feed خودش خودش را قبل از خواندن اعتبارسنجی می‌کند، به‌ویژه اگر redirect می‌خواهد به میزبانی که allowlist ندارد.
- `PostgresCandidateRepository.RegisterDiscovered` رقابت را با گرفتن داده‌های تازه از DB و پاک کردن change tracker حل می‌کند، نه با retry نامحدود.
- `IScriptGenerator` پورت است. `DisabledScriptGenerator` بدون مدل و بدون API key validate می‌کند و خطای قابل‌اقدام می‌دهد؛ هرگز نمی‌کوشد template را به‌صورت uncited AI تقلب کند. `ScriptDraftComposer` حذف شد چون دلیلش ناپدید شده بود.
- `ScriptGeneration` با `ScriptVersion` در یک write ثبت می‌شود، پس `AddGeneratedScript` هیچ اثر جانبی ندارد، اما `SaveVersion` و `Save` را اجرا می‌کند تا نمایش `GetScripts` همواره واقعاً موجود باشد.
- `ResearchPolicy` حداقل دو منبع با `ReliabilityScore >= 50` و excerpt می‌طلبد. یک پژوهش با منبع واحد هرگز pack نمی‌شود، حتی اگر یکی از آنها reliability ۱۰۰ داشته باشد.
- یک `DiscoveryWorker` timer-based هر feed allowlisted را بازبینی می‌کند. یک `ResearchWorker` هر Candidate را بررسی می‌کند و وقتی pack آماده باشد آن را می‌سازد، بدون اینکه Candidateهای ناقص را مجبور به پردازش کند.
- migration جدید `20260926103424_DiscoveryAndScriptProvenance` ستون `fingerprint` و جدول `script_generations` اضافه می‌کند. فیلد fingerprint به‌صورت nullable اضافه شد، سپس SQL backfill همان `CandidateFingerprint.From` را روی هر ردیف اجرا می‌کند، بعد ردیف‌های colliding با id خودشان salted می‌شوند و بعد NOT NULL index ساخته می‌شود. به همین ترتیب هیچ داده‌ای از بین نمی‌رود. backfill SQL همان hash domain را تولید می‌کند و تست `FingerprintBackfillTests` این تطابق را با رشته‌های نمونه، از جمله فارسی، تأیید می‌کند.
- `ServiceCollectionExtensions` `ViralScoreWeights` را singleton می‌سازد، `DiscoveryOptions.Weights` را از config bind می‌کند و `ViralScoreWeightsOptions.EnsureNoUnknownKeys` وزن‌های ناشناخته را پیدا و loud صدا می‌زند تا اشتباه تایپی مثل `Engagement` به‌جای `ViewVelocity` باعث rescale خاموز هر score نشود.
- `DiscoveryOptions.ToWeights` یک extension روی configuration است تا تست‌های domain بتوانند بدون DI واقعی config مصنوعی بسازند.
- Workers هیچ `appsettings.json` نداشتند و startup crash می‌کردند. حالا `appsettings.json` و `appsettings.Development.json` با دیسکاوری پیکربندی‌شده هستند.
- `ProductionEndpoints` endpointهای جدید برای `GET /productions/{id}/scripts` و `GET /productions/{id}/script-generations` اضافه کرد، و `POST /productions/{id}/scripts/draft-from-research` حالا `WorkflowRuleViolation` و `InvalidOperationException` را به‌صورت جداگانه به ۴۰۹ و ۴۰۹/۴۰۰ تبدیل می‌کند.
- `CandidateEndpoints` `/candidates/discovery` اضافه کرد تا یک feed story را به‌صورت دستی ثبت کرد و ببینیم چه می‌شود؛ repeat هیچ خطایی نمی‌دهد بلکه candidate قبلی را برمی‌گرداند.

### کار باقی‌مانده

- صف job همچنان in-memory است و در deployment باید پایدار شود.
- `PostgresCandidateRepository.TryGet` از `Find` استفاده می‌کند که change tracker را درگیر می‌کند. برای خواندن‌های صرفاً query باید `AsNoTracking` جایگزین شود. در `PostgresProductionRepository` این کار عمداً انجام نشد: داشتن entity ردیابی‌شده همان چیزی است که نسخهٔ اصلی را برای نوشتن نگه می‌دارد.
