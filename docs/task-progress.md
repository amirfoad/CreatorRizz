# CreatorRizz Progress

این فایل، وضعیت بخش‌هایی را ثبت می‌کند که در کد پروژه انجام و بررسی شده‌اند.

- [x] پایه ASP.NET Core، worker، Docker Compose و health checks؛ health فقط وجود config را می‌سنجد و نه دیتابیس، صف یا provider را
- [x] state machine و گیت‌های بازبینی Script، Rights و Publish
- [x] Candidate intake، جلوگیری از تکرار و ViralScore v1
- [x] Research Pack، claim map و versioning اسکریپت
- [x] policy حقوق asset و جلوگیری از render دارایی نامشخص
- [x] manifest و SRT؛ کار render و TTS در جدول `job_outbox` ثبت می‌شود، ولی هنوز consumer ندارد و هیچ workerای آن را برنمی‌دارد
- [x] object storage محلی امن و schema اولیه PostgreSQL؛ MinIO و Redis در config اعتبارسنجی می‌شوند ولی هنوز adapter فعالی ندارند
- [x] audit trail، correlation ID، security header و rate limit
- [x] parser RSS برای discovery اولیه
- [x] لوگو و tokenهای رنگ CreatorRizz در رابط وب
- [x] راهنمای توسعه AGENTS.md با اصول Clean Architecture، KISS، YAGNI و DRY
- [x] بازبینی invariantهای Candidate، Research Pack و publish asset انجام شد
- [x] انتقال persistence موقت از API به Infrastructure/Persistence و تفکیک مرز HTTP از storage
- [x] تفکیک Program.cs به composition root، Contracts، Endpoints و Results در لایه API
- [x] انتقال correlation و security middlewareهای API به پوشه Middleware
- [x] Swagger/OpenAPI توسعه‌ای برای endpointهای API
- [x] تفکیک لایه Domain به مدل‌ها، policyها، workflow، rendering، captions، scoring و scripting
- [x] ایجاد CreatorRizz.Application با use caseهای CreatorRizz و portهای persistence/job queue
- [x] تفکیک CreatorRizz.Infrastructure بر اساس adapterهای configuration، persistence، jobs، storage، discovery، TTS و publishing
- [x] مرتب‌سازی solution folderهای Visual Studio: لایه‌ها زیر src و تست‌ها زیر tests
- [x] persistence کامل روی PostgreSQL: `PostgresCandidateRepository` و `PostgresProductionRepository` به‌عنوان تنها adapterها
- [x] EF Core به‌عنوان تنها مالک schema با migration اولیه و حذف SQL دستی
- [x] اجرای migration در startup پیش از پذیرش ترافیک
- [x] asset usage با narrative purpose و زمان‌بندی که از render manifest پر می‌شود
- [x] transitionهای مفقود state machine: `ScriptApproved → AssetsPreparing → AssetsReady` و `Rendering → Rendered`
- [x] تست integration روی PostgreSQL واقعی با دیتابیس موقت در هر اجرا
- [x] enumهای JSON به‌صورت نام؛ عدد دیگر پذیرفته نمی‌شود
- [x] optimistic concurrency با `If-Match`/`ETag` و concurrency token در EF Core
- [x] lockfile و typeهای React برای عبور `npm ci` و `npm run typecheck`
- [x] اصلاح نام solution در CI و افزودن سرویس PostgreSQL به آن؛ CI فقط `workflow_dispatch` است و PR را نگه نمی‌دارد
- [x] fingerprint SHA-256 برای dedupe نام‌شناسا و index unique
- [x] `ViralScoreWeights` با مجموع ۱۰۰ و validation در runtime و config با نام‌های ناشناسه که صدا می‌زند
- [x] `IDiscoverySource` و `IScriptGenerator` پورت‌ها؛ `RssDiscoverySource` با allowlist و `DisabledScriptGenerator`
- [x] research pack از حداقل دو منبع قابل‌استفاده و تفکیک facts/uncertainty
- [x] ثبت اتمی `ScriptGeneration` با model/prompt/input references و audit
- [x] worker discovery و research به‌صورت timer-based، بدون اعمال feed غیرمجاز
- [x] migration جدید برای fingerprint با backfill SQL که همان domain hash تولید می‌کند و `script_generations`
- [x] تایمینگ نامشخص asset هنوز محدود به render manifest
- [x] lint و typecheck فرانت‌اند
- [x] بازبینی صداقت ادعاهای این فایل در برابر کد؛ موارد نادرست اصلاح و در `docs/architecture-review.md` ثبت شد
- [x] اصلاح باگ واقعی validation وزن‌ها: مسیر startup حالا از `IConfiguration` اعتبارسنجی‌کننده می‌خواند، نه از `IOptions` بی‌اعتبار
- [x] بررسی allowlist در هر hop از redirect در `RssDiscoverySource`، چون handler پیش‌فرض redirect را خودش دنبال می‌کرد
- [x] `OpenAiCompatibleScriptGenerator` واقعی با رد کردن claimهای دارای `sourceId` ناشناخته
- [x] `ScriptGenerationFailedException` و نگاشت آن به ۵۰۲، جدا از ۴۰۹ِ draft رد‌شده
- [x] ثبت `ITextToSpeechProvider` در DI با تست fail-fast؛ provider واقعی هنوز نیست
- [x] اصلاح ساخت endpoint اسکریپت‌ساز؛ `BaseUrl` که طبق قرارداد OpenAI با `/v1` تمام می‌شود حالا `/v1/v1/chat/completions` نمی‌سازد
- [x] صف پایدار به‌جای صف in-memory: جدول `job_outbox` در migration `RecordQueuedWork`
- [x] ثبت اتمیک enqueue؛ `QueueRenderAsync` تغییر وضعیت و ثبت کار را در یک transaction می‌نویسد
- [x] `IWorkflowTransaction` در Application و `PostgresWorkflowTransaction` در Infrastructure، هر دو روی یک context اشتراکی
- [x] کلید idempotency با ایندکس یکتا روی `idempotency_key`؛ enqueue تکراری یک کار است نه دو
- [ ] provider واقعی TTS هنوز نیست؛ FFmpeg به‌عنوان executor اضافه شد ولی روی این ماشین نصب نیست، پس اجرای واقعی encode تأیید نشده
- [x] `IRenderExecutor` در DI و `RenderJobWorker` که ردیف‌های `job_outbox` را با lease و `FOR UPDATE SKIP LOCKED` claim، اجرا و کامل یا آزاد می‌کند
- [x] احراز هویت API: JWT bearer با `FallbackPolicy` روی همهٔ endpointها، نقش `reviewer` از claim توکن و حذف نقش از هدر `X-Role` و از بدنهٔ درخواست
- [x] endpoint فهرست production و صف بازبینی: `GET /productions` با فیلتر `state` و `awaitingReview`، صفحه‌بندی محدود و `TotalCount`
- [ ] React operations dashboard؛ رابط فعلی فقط صفحهٔ برند است و API را صدا نمی‌زند
- [ ] OAuth و upload private به YouTube؛ `IYouTubePublisher` هنوز به هیچ endpoint یا transitionی وصل نیست
- [ ] پایلوت 3 Short در روز

## بررسی مسیر محصول اشتراکی — 2026-09-26

- [x] بررسی کد فعلی و مستندات رسمی vidIQ؛ ثبت شکاف‌های محصول، تولید و فروش در [Roadmap](../.agents/ROADMAP.md)
- [x] ثبت مسیر پیشنهادی YouTube و آپارات با تفکیک قابلیت مستند از دسترسی API تأییدنشده
- [x] ثبت درخواست محصول بین‌المللی: فارسی/انگلیسی و نمایش آپارات فقط برای بازار ایران
- [ ] رابط و مسیر مشتری دوزبانه، زبان مستقل محتوا و نمایش منطقه‌ای پلتفرم
- [ ] اثبات قابلیت‌های API آپارات و الزامات داده/مجوز YouTube برای محصول تحلیلی
- [ ] حساب، workspace، نقش معتبر و جداسازی اطلاعات مشتریان
- [ ] اتصال کانال، دریافت داده و داشبورد رشد قابل استفاده
- [ ] پرداخت، اشتراک، مجوز استفاده و دفتر مصرف اعتبار
- [ ] صف پایدار، worker تولید واقعی و storage مشترک

مصرف صف برای Render انجام شد (پایین). TTS، FFmpeg واقعی و storage اشتراکی میان processها هنوز کامل نیست.
- [ ] استقرار production و پایلوت پولی با provider واقعی

این نوبت پژوهش و مستندسازی بود؛ کد تغییر نکرد و build/test تازه اجرا نشد. موارد فوق به معنی اجرای قابلیت یا آمادگی فروش نیستند.

## نوبت بعد: بازبینی صداقت و provider واقعی اسکریپت — 2026-09-26

این نوبت کد تغییر کرد. ممیزی کد در برابر ادعاهای همین فایل، چند مورد `[x]` را نادرست نشان داد؛ هر مورد یا در کد اصلاح شد یا در متن مستندات صادقانه شد.

- باگ واقعی validation وزن‌ها رفع شد: مسیر startup از `IOptions` بی‌اعتبار می‌خواند، پس وزن با غلط املایی بی‌سروصدا defaults را نگه می‌داشت. تست تازه خود composition root را می‌آزماید، چون تست قبلی فقط extension را می‌آزمود و دقیقاً همین شکاف را از کسور گذاشته بود.
- allowlist مربوط به redirect اصلاح شد؛ پیش از این، یک feed مجاز می‌توانست به هر میزبانی برود.
- `OpenAiCompatibleScriptGenerator` اضافه شد و claimهای دارای `sourceId` ناشناخته را رد می‌کند. `ScriptGenerationFailedException` با ۵۰۲ از ۴۰۹ِ draft رد‌شده جدا شد.
- `ITextToSpeechProvider` که در DI ثبت نشده بود ثبت شد، ولی provider واقعی هنوز نیست.

**آنچه اجرا نشد:** `CreatorRizz.Persistence.Tests` در این نوبت اجرا نشد. دسترسی به registry داکر denied بود، PostgreSQL بالا نیامد و هر ۳۲ تست integration شکست خوردند. این شکست محیطی است، نه تأیید سلامت آن‌ها. `dotnet build` بدون warning، ۵۷ تست `CreatorRizz.Domain.Tests`، و در وب `npm ci`، `npm run typecheck` و `npm run build` همگی سبز بودند.

## نوبت بعد: احراز هویت API و هم‌ترازی مستندات — 2026-09-27

کد احراز هویت در commit `8748a9f` نوشته شده بود ولی هیچ‌کدام از README، همین فایل و `docs/architecture-review.md` به‌روز نشده بودند، بنابراین هر سه هنوز می‌گفتند API احراز هویت ندارد. این نوبت ادعاها را با کد هم‌تراز کرد.

- احراز هویت وجود دارد: `ApiAuthentication` در `src/CreatorRizz.Api/Authentication`، JWT bearer با `MapInboundClaims = false` و `FallbackPolicy` روی همهٔ endpointها. فقط `/health/*` با `AllowAnonymous` باز است. هدر `X-Role` و فیلد بازبین در بدنهٔ درخواست حذف شده‌اند و `ReviewerAccess.ReadId` نام بازبین را از claim `sub` می‌خواند.
- **سه تست از ده تست `ReviewerGateTests` شکست می‌خوردند و هیچ‌چیز را واقعاً تأیید نمی‌کردند.** `BearerOptions` یک `ServiceCollection` خالی را build می‌کرد، ولی `LocalSigningKeysFromUserJwts` در سازنده‌اش `IConfiguration` می‌گیرد. خطای resolve جای `OptionsValidationException`ی را می‌گرفت که تست می‌خواست بسنجد، پس تست‌های «startup منبع توکن را رد می‌کند» هرگز آن شرط را نرسیدند. یک helper مشترک اضافه شد که configuration را در container ثبت می‌کند، همان‌طور که host واقعی می‌کند.
- **آنچه اجرا شد:** `dotnet build` در Release بدون warning و هر ۶۸ تست `CreatorRizz.Domain.Tests` سبز، شامل هر ۱۰ تست گیت بازبین.
- **آنچه اجرا نشد:** `CreatorRizz.Persistence.Tests`. daemon داکر در این محیط بالا نیست، پس PostgreSQL در دسترس نبود و هر ۳۵ تست integration شکست خوردند. این شکست محیطی است، نه تأیید سلامت آن‌ها.

محدودیتی که باقی است و در README ثبت شد: مدل دسترسی فقط «توکن معتبر» و «نقش reviewer» است. جداسازی مشتری، workspace و مالکیت production هنوز در مدل وجود ندارد.

## نوبت بعد: مصرف صف render و executor واقعی FFmpeg — 2026-09-27

این نوبت شکاف بزرگ مسیر render را بست: کاری که `QueueRenderAsync` ثبت می‌کرد هیچ‌کس آن را برنمی‌داشت، و `FfmpegRenderExecutor` هم فقط `ffmpeg -version` می‌زد و خطا می‌داد.

- `job_outbox` ستون‌های `claimed_at`, `claimed_by`, `completed_at`, `attempts`, `last_error` گرفت و migration `ClaimOutboxWork` آن‌ها را اضافه کرد. حذف ردیف جای lease نشست: کاری که workerش می‌میرد خودش برمی‌گردد و دلیل شکست روی ردیف می‌ماند.
- `IProductionJobDispatcher` در Application و `PostgresProductionJobDispatcher` در Infrastructure. claim یک `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED LIMIT 1)` است، پس دو worker هرگز یک ردیف را نمی‌گیرند و بازنده پشت قفل هم نمی‌ایستد بلکه ردیف بعدی را برمی‌دارد.
- `RenderJobWorker` فقط job نوع `Render` را claim می‌کند. `TextToSpeech` عمداً باقی ماند چون provider واقعی و محل ثبت خروجی صوتی وجود ندارد.
- **اشتباهی که تست نگرفت و تست گرفت:** اولین نسخه `RenderJobWorker` نسخهٔ production را حدس می‌زد. کار، نسخهٔ فعلی production را از دیتابیس می‌خواند و همان را به `CompleteRendering` می‌دهد تا قاعدهٔ If-Match دور زده نشود.
- **دو باگ که فقط با build/test بیرون آمدند:** `FfmpegRenderExecutor` و جدول outbox هر دو اضافه شده بودند ولی برنامه اصلاً کامپایل نمی‌شد؛ `using CreatorRizz.Workers;` از `Program.cs` حذف شده بود و helperها `IServiceProvider` می‌گرفتند و `ServiceProvider` صدا می‌زدند. بعد `SingleOrDefaultAsync` روی خروجی `FromSql` خطا داد، چون EF روی `UPDATE` دوباره SQL نمی‌چسباند.
- **اشکال تست که خودم ساختم:** تست‌های dispatcher ابتدا ردیف کار دیگر تست‌ها را claim می‌کردند، چون outbox یک صف مشترک است نه فهرست هر production. حالا هر تست با `EmptyOutbox()` شروع می‌شود و این محدودیت در کامنت آمده، نه در تنظیمات مخفی.
- **یک اشکال طراحی که پیش از تست پیدا شد:** `ObjectStorage:RootPath` در هر دو appsettings خالی بود و به `AppContext.BaseDirectory` برمی‌گشت، یعنی هر process پوشهٔ خودش را می‌ساخت و worker هرگز فایلی را که API ذخیره کرده نمی‌دید. حالا مقدار پیش‌فرض `storage/objects` است و مقدار خالی رد می‌شود.
- **آنچه اجرا شد:** `dotnet build` در Release بدون warning، ۷۶ تست `CreatorRizz.Domain.Tests` (۸ تست تازهٔ `FfmpegArgumentTests`) و ۴۲ تست `CreatorRizz.Persistence.Tests` روی PostgreSQL واقعی، شامل ۷ تست تازهٔ dispatcher.
- **آنچه اجرا نشد:** FFmpeg روی این ماشین نصب نیست، پس هیچ encode واقعی اجرا نشد. تست‌ها فقط ساخت دستور را می‌سنجند: ترتیب inputها، `-ss`/`-t` هر کلیپ، ترتیب بر اساس زمان‌بندی و escape مسیر subtitle ویندوز. provider واقعی TTS هم هنوز وجود ندارد. چک‌های فرانت‌اند هم اجرا نشد چون `node_modules` در این محیط نصب نیست (`tsc` پیدا نشد) و در این نوبت هیچ فایل وبی تغییر نکرد.

## نوبت بعد: فهرست production و صف بازبینی — 2026-09-27

بدون این endpoint، صف بازبینی از API قابل ساخت نبود: فقط خواندن با id ممکن بود و هیچ راهی نبود که بازبین ببیند چه چیزی منتظر تصمیم اوست.

- `GET /productions` با فیلترهای `state` و `awaitingReview`، `skip` و `take`. صف بازبینی endpoint جدا نیست چون همان ردیف‌های همان جدول‌اند؛ یک production که منتظر تصمیم است همان production است که state‌اش این را می‌گوید.
- **نگاشت state به نوع بازبینی در `ProductionWorkflow.ReviewAwaitingDecision` زندگی می‌کند**، کنار همان transitionهایی که آن state را می‌سازند. اگر query این نگاشت را دوباره در خودش می‌داشت، دومین جایی بود که باید با گذر زمان هماهنگ بماند. تست `EveryWaitingStateIsReachableBySubmittingThatSameReview` دو سر نگاشت را به هم می‌دوزد.
- صفحه محدود است و `TotalCount` شمارش کل فیلتر است نه شمارش صفحه، وگرنه کلاینت نمی‌تواند صفحه‌بندی کند. `HasMore` به `Take` نیاز داشت: بدون آن یک صفحهٔ ناقص در انتهای نتایج «صفحهٔ بعدی» گزارش می‌کرد که وجود ندارد و داشبورد دکمهٔ next را برای همیشه نشان می‌داد. این را اولین تست نوشتم و خودِ تست اشتباه بود.
- نسخهٔ production در هر ردیف صف برگردانده می‌شود، چون بازبین باید همان را به‌عنوان `If-Match` بفرستد. حدس زدن نسخه یعنی گرفتن conflict به‌جای بازنویسی کار بازبین دیگر.
- **اشتباه خودم در تست:** اول نسخهٔ کلاس را طوری نوشتم که کل صفحه را برمی‌گرداند و بعد با اسکوپ‌دار کردن درستش کردم. دیتابیس fixture بین کل collection مشترک است، پس «صف دقیقاً این را دارد» بدون محدود کردن به productionهای خودِ تست روی ردیف تست قبلی می‌شکست. helper `Mine` این محدودیت را در کامنت خودش نگه می‌دارد.
- **همین آلودگی یک تست قدیمی را هم خراب کرد:** `AWorkerThatOnlyHandlesRendersLeavesSpeechRowsAlone` انتظار داشت هیچ ردیف Render قابل claimی وجود نداشته باشد، ولی تست‌های تازه ردیف Render جا می‌گذاشتند. حالا آن هم اول `EmptyOutbox()` می‌کند.
- **آنچه اجرا شد:** `dotnet build` در Release بدون warning، ۸۶ تست `CreatorRizz.Domain.Tests` (۱۰ تست تازهٔ `ReviewQueueTests`) و ۴۷ تست `CreatorRizz.Persistence.Tests` روی PostgreSQL واقعی (۵ تست تازهٔ صف و صفحه‌بندی).
- **آنچه اجرا نشد:** هیچ تست HTTP وجود ندارد؛ این پروژه harness ای برای بالا آوردن API ندارد و برای همین رفتار endpoint از جمله ۴۰۰ِ فیلتر ناشناس با تست پوشش داده نشده. نگاشت فیلتر و خود query تست شده‌اند ولی لایهٔ HTTP نه. چک فرانت‌اند هم اجرا نشد چون `node_modules` نصب نیست.

آخرین به‌روزرسانی: 2026-09-27
