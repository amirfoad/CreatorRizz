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
- `IDiscoverySource` پورت است و `DiscoveryFeedOptions.AllowedHosts` می‌گوید چه feedهایی اجازه خواندن دارند. هر feed خودش را قبل از خواندن اعتبارسنجی می‌کند و redirect‌ها را دستی دنبال می‌کند تا هر hop جداگانه در allowlist بررسی شود؛ `HttpClient` عمداً با `AllowAutoRedirect = false` ساخته می‌شود، چون handlerای که خودش redirect را دنبال کند قبل از دیدن پاسخ، میزبان تأییدنشده را می‌خواند.
- `PostgresCandidateRepository.RegisterDiscovered` رقابت را با گرفتن داده‌های تازه از DB و پاک کردن change tracker حل می‌کند، نه با retry نامحدود.
- `IScriptGenerator` پورت است. `DisabledScriptGenerator` وقتی هیچ credentialای پیکربندی نشده ثبت می‌شود، بدون مدل validate می‌کند و خطای قابل‌اقدام می‌دهد؛ هرگز نمی‌کوشد template را به‌صورت uncited AI تقلب کند. `OpenAiCompatibleScriptGenerator` هر endpoint سازگار با شکل OpenAI chat completions را می‌خواند. `ScriptDraftComposer` حذف شد چون دلیلش ناپدید شده بود.
- `ScriptGeneration` با `ScriptVersion` در یک write ثبت می‌شود، پس `AddGeneratedScript` هیچ اثر جانبی ندارد، اما `SaveVersion` و `Save` را اجرا می‌کند تا نمایش `GetScripts` همواره واقعاً موجود باشد.
- `ResearchPolicy` حداقل دو منبع با `ReliabilityScore >= 50` و excerpt می‌طلبد. یک پژوهش با منبع واحد هرگز pack نمی‌شود، حتی اگر یکی از آنها reliability ۱۰۰ داشته باشد.
- یک `DiscoveryWorker` timer-based هر feed allowlisted را بازبینی می‌کند. یک `ResearchWorker` هر Candidate را بررسی می‌کند و وقتی pack آماده باشد آن را می‌سازد، بدون اینکه Candidateهای ناقص را مجبور به پردازش کند.
- migration جدید `20260926103424_DiscoveryAndScriptProvenance` ستون `fingerprint` و جدول `script_generations` اضافه می‌کند. فیلد fingerprint به‌صورت nullable اضافه شد، سپس SQL backfill همان `CandidateFingerprint.From` را روی هر ردیف اجرا می‌کند، بعد ردیف‌های colliding با id خودشان salted می‌شوند و بعد NOT NULL index ساخته می‌شود. به همین ترتیب هیچ داده‌ای از بین نمی‌رود. backfill SQL همان hash domain را تولید می‌کند و تست `FingerprintBackfillTests` این تطابق را با رشته‌های نمونه، از جمله فارسی، تأیید می‌کند.
- `ServiceCollectionExtensions` `ViralScoreWeights` را singleton می‌سازد، `DiscoveryOptions.Weights` را از config bind می‌کند و `ViralScoreWeightsOptions.EnsureNoUnknownKeys` وزن‌های ناشناخته را پیدا و loud صدا می‌زند تا اشتباه تایپی مثل `Engagement` به‌جای `ViewVelocity` باعث rescale خاموز هر score نشود. این ادعا تا وقتی نادرست بود که مسیر واقعی startup از `IOptions<DiscoveryOptions>.Value` می‌خواند و `EnsureNoUnknownKeys` را صدا نمی‌زد؛ یعنی فقط تست‌ها آن را می‌دیدند و یک وزن با غلط املایی در `appsettings.json` بی‌سروصدا defaults را نگه می‌داشت. حالا `AddCreatorRizzInfrastructure` از همان مسیر اعتبارسنجی‌کنندهٔ `IConfiguration` می‌خواند و `EnsureValid` را صدا می‌زند. جزئیات در بخش «بازبینی صداقت مستندات» پایین همین سند.
- `DiscoveryOptionsExtensions.ToWeights` یک extension روی **configuration ریشه** است. گرفتن `GetSection("Discovery")` و دادن آن به این extension مسیر را به `Discovery:Discovery` دوتایی می‌کند و چیزی اعتبارسنجی نمی‌شود؛ این اشتباه در بازبینی پیدا و اصلاح شد.
- Workers هیچ `appsettings.json` نداشتند و startup crash می‌کردند. حالا `appsettings.json` و `appsettings.Development.json` با دیسکاوری پیکربندی‌شده هستند.
- `ProductionEndpoints` endpointهای جدید برای `GET /productions/{id}/scripts` و `GET /productions/{id}/script-generations` اضافه کرد، و `POST /productions/{id}/scripts/draft-from-research` حالا `WorkflowRuleViolation` و `InvalidOperationException` را به‌صورت جداگانه به ۴۰۹ و ۴۰۹/۴۰۰ تبدیل می‌کند.
- `CandidateEndpoints` `/candidates/discovery` اضافه کرد تا یک feed story را به‌صورت دستی ثبت کرد و ببینیم چه می‌شود؛ repeat هیچ خطایی نمی‌دهد بلکه candidate قبلی را برمی‌گرداند.

### بازبینی صداقت مستندات و provider واقعی اسکریپت

این نوبت با ممیزی کد در برابر ادعاهای `docs/task-progress.md` و README شروع شد. چند مورد `[x]` در واقع برقرار نبودند.

- **ادعای نادرست دربارهٔ validation وزن‌ها.** گفته شده بود نام وزن ناشناخته در runtime صدا می‌زند. در کد، `EnsureNoUnknownKeys` فقط در extension روی `IConfiguration` صدا زده می‌شد و آن extension فقط از تست‌ها فراخوانی می‌شد؛ `AddCreatorRizzInfrastructure` از `IOptions<DiscoveryOptions>.Value` می‌خواند که هیچ اعتبارسنجی ندارد. باگ واقعی بود و در startup پروژه اثر داشت. رفع شد و `InfrastructureCompositionTests` حالا خود composition root را می‌آزماید، چون تست قبلی فقط extension را می‌آزمود و دقیقاً همین شکاف را از کسور گذاشته بود.
- **ادعای نادرست دربارهٔ allowlist در redirect.** گفته شده بود feed هر hop را بررسی می‌کند، ولی `HttpClient` پیش‌فرض redirect را خودش دنبال می‌کرد و میزبان نهایی هرگز دیده نمی‌شد. یک feed مجاز می‌توانست به هر میزبانی برود. `RssDiscoverySource` حالا redirect را دستی دنبال می‌کند و هر hop را با `EnsureHostAllowed` می‌سنجد.
- **ادعای بزرگ‌نمایی‌شده دربارهٔ صف TTS و render.** manifest validator و `SrtSubtitleWriter` واقعی و تست‌شده‌اند، ولی هیچ consumerای برای صف وجود ندارد. متن مستندات اصلاح شد. (بعداً صف in-memory هم حذف و با جدول PostgreSQL جایگزین شد؛ نگاه کنید به بخش `job_outbox`.)
- **`ObjectStorageEndpoint` و `RedisConnectionString` تزئینی‌اند.** هر دو در startup اعتبارسنجی می‌شوند و بعد استفاده نمی‌شوند؛ `LocalObjectStorage` تنها adapter فعال است. سرویس MinIO در `docker-compose.yml` هیچ clientای ندارد.
- **رابط وب «operations dashboard» نیست.** `web/src/main.tsx` یک صفحهٔ برند با یک جملهٔ placeholder است و اصلاً API را صدا نمی‌زند.
- **`CreatorRizz.Persistence.Tests` اجرا شد و سبز است.** تلاش اول به‌خاطر denied بودن دسترسی registry داکر شکست خورد. در نوبت بعد یک PostgreSQL محلی بالا آمد و **۳۵ تست integration پاس شدند**. دو نکتهٔ محیطی: یک PostgreSQL بومی ویندوز روی `127.0.0.1:5432` از قبل اشغال بود، پس `Host=localhost` به سرور اشتباه می‌رفت و تست‌ها با `28P01 password authentication failed` می‌مردند؛ و ظرفیت `localhost` که به `::1` و `127.0.0.1` هر دو resolve می‌شود مبهم است. به همین دلیل `PostgresFixture` متغیر محیطی `CREATORRIZZ_TEST_POSTGRES` را نگه می‌دارد و اجرای تست باید صریح به یک `Host` بدهد. `dotnet build` بدون warning و ۵۸ تست `CreatorRizz.Domain.Tests` هم سبز هستند.

#### `job_outbox` و ثبت اتمیک کار

- صف in-memory (`InMemoryBackgroundJobQueue`، `InMemoryProductionJobQueue`) حذف شد. آن صف با `Channel.CreateBounded` کار می‌کرد، نه مصرف‌کننده داشت، نه با restart زنده می‌ماند، و `DequeueAsync`ش فقط در یک تست استفاده می‌شد.
- `JobOutboxEntry` یک entity صرفاً Infrastructure است، چون outbox یک سازوکار زیرساختی است نه یک مفهوم دامنه. `ProductionJobKind` هم یک enum واقعی است، نه رشته‌ای مثل `"tts"` که تایپ نمی‌شد.
- `QueueRenderAsync` حالا از `IWorkflowTransaction` استفاده می‌کند تا تغییر وضعیت و ثبت کار یک commit باشند. این تنها جایی است که شکاف واقعی داشت: وضعیت `Rendering` می‌شد و *بعد* enqueue، پس crash بین این دو production را برای همیشه معلق می‌گذاشت و هیچ چیزی آن را reconcile نمی‌کرد. تست `ARenderThatCannotBeRecordedLeavesTheProductionOutOfRendering` همین رفتار را روی PostgreSQL واقعی قفل می‌کند.
- کلید idempotency از **نسخه‌ای که درخواست‌دهنده خوانده** ساخته می‌شود، نه `expectedVersion + 1`. خواندن README سیاست صریحی دارد که هیچ‌جا نسخهٔ بعدی حدس زده نشود، و اینجا همان قاعده اعمال شد.
- **ستون‌های claim و completion اضافه نشدند.** هیچ consumerی وجود ندارد، پس هر ستونی که کسی نمی‌نویسد فقط مدل را جلوتر از واقعیت می‌برد. با آمدن dispatcher یک migration دیگر اضافه می‌شود؛ همان الگویی که قبلاً برای `fingerprint` در همین ریپو اجرا شده.
- یک نکتهٔ تست که ارزش ثبت دارد: `PostgresFixture` یک دیتابیس برای کل collection می‌سازد، پس شمارش سطرهای جدول مشترک به ترتیب اجرای تست‌ها وابسته است. هر assertion مربوط به `job_outbox` به `production_id` خودش محدود شد.

#### `OpenAiCompatibleScriptGenerator`

- adapter جدید هر endpoint سازگار با شکل OpenAI chat completions را می‌خواند، پس یک کد برای مدل میزبانی‌شده و مدل محلی کافی است.
- مدل یک‌بار اسکریپت و claim map را برمی‌گرداند. claim map پیش از بازگشت به workflow در برابر منبع‌هایی که مدل گرفته بررسی می‌شود: **هر claim که source id ناشناخته دارد کل پاسخ دور ریخته می‌شود.** پذیرفتن آن یعنی متنی که بازبین نمی‌تواند راستی‌آزمایی کند وارد گیت بازبینی اسکریپت شود، که همان چیزی است که این workflow برای جلوگیری از آن ساخته شده.
- فقط منبع‌هایی به مدل داده می‌شوند که `ResearchPolicy` پشتشان می‌ایستد، یعنی reliability کافی و excerpt داشتن. این با شکل facts در `BuildResearchPackFromSources` یکی است.
- `ScriptGenerationFailedException` اضافه شد. عمداً از `InvalidOperationException` ارث نمی‌برد، چون درخواست سالم بوده و هیچ گیتی مقصر نیست؛ endpoint آن را ۵۰۲ می‌دهد نه ۴۰۹. کلاینتی که ۴۰۹ می‌بیند دست می‌کشد و کلاینتی که ۵۰۲ می‌بیند دوباره تلاش می‌کند.
- `BaseUrl` باید https باشد، مگر روی loopback. کلید به‌صورت bearer token فرستاده می‌شود و redirect دنبال نمی‌شود تا آن کلید به میزبانی که اپراتور نام نبرده دوباره فرستاده نشود.
- **باگ در ساخت endpoint پیدا و رفع شد.** `BaseUrl` طبق قرارداد OpenAI با `/v1` تمام می‌شود، ولی adapter مسیر `"v1/chat/completions"` را به‌صورت نسبی روی آن resolve می‌کرد و نتیجه `/v1/v1/chat/completions` می‌شد. تست قبلی همین مسیر غلط را تثبیت کرده بود، پس تست به‌جای گرفتن اشتباه، آن را تأیید می‌کرد. حالا فقط `chat/completions` به `BaseUrl` اضافه می‌شود و برای `BaseUrl` با و بدون slash انتهایی تست دارد.
- وقتی credential پیکربندی نشده، `DisabledScriptGenerator` ثبت می‌شود تا نبودِ کلید باعث نشود سرویس بالا نیاید؛ خطا در همان گیت draft و با پیام قابل‌اقدام داده می‌شود.
- `ITextToSpeechProvider` که اصلاً در DI ثبت نشده بود حالا ثبت شده است. provider واقعی هنوز نیست و همچنان خطا می‌دهد.

### کار باقی‌مانده

- **provider واقعی TTS و FFmpeg نیست.** `FfmpegRenderExecutor` فقط `ffmpeg -version` اجرا می‌کند و بعد `NotSupportedException` می‌دهد؛ نه filter graph دارد، نه آرگومان از manifest، نه burn-in زیرنویس. ضمناً `IRenderExecutor` در DI ثبت نشده و هیچ `BackgroundService`ای برای render وجود ندارد.
- **`IYouTubePublisher` به هیچ چیزی وصل نیست.** نه endpoint، نه متد repository، نه transition. `ProductionState.Uploading` و `Published` enum هستند ولی هیچ کدی آن‌ها را نمی‌نویسد.
- صف حالا پایدار است (جدول `job_outbox`) و enqueue با تغییر وضعیت اتمیک است، ولی **هنوز هیچ consumerای وجود ندارد**. ردیف‌های ثبت‌شده هرگز برداشته نمی‌شوند، پس هیچ render و TTS اجرا نمی‌شود و هیچ `CompleteRendering`ای صدا زده نمی‌شود. `IRenderExecutor` هم در DI ثبت نشده است. قدم بعدی یک `BackgroundService` است که ردیف را claim کند، اجرا کند و نتیجه را بنویسد.
- **API احراز هویت ندارد.** نقش بازبین از هدر `X-Role` و شناسهٔ بازبین از بدنهٔ درخواست خوانده می‌شود. تا وقتی این هست، audit می‌تواند actor نامعتبر داشته باشد و این قبل از هر کار دیگری باید رفع شود.
- CI فقط `workflow_dispatch` است؛ `push` و `pull_request` ندارد، پس نمی‌تواند جلوی یک PR را بگیرد.
- health check فقط وجود چند رشتهٔ config را می‌سنجد؛ دیتابیس، صف، FFmpeg و providerها را بررسی نمی‌کند.
- rate limiter یک bucket مشترک با کلید ثابت `"api"` دارد، پس کل ترافیک از جمله `/health/*` در یک سهمیهٔ ۶۰ در دقیقه جمع می‌شود.
- `PostgresCandidateRepository.TryGet` از `Find` استفاده می‌کند که change tracker را درگیر می‌کند. برای خواندن‌های صرفاً query باید `AsNoTracking` جایگزین شود. در `PostgresProductionRepository` این کار عمداً انجام نشد: داشتن entity ردیابی‌شده همان چیزی است که نسخهٔ اصلی را برای نوشتن نگه می‌دارد.
