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
- [ ] provider واقعی TTS و FFmpeg؛ `FfmpegRenderExecutor` فقط `ffmpeg -version` می‌زند و خطا می‌دهد
- [ ] `IRenderExecutor` در DI و `BackgroundService` که ردیف‌های `job_outbox` را claim و اجرا کند
- [ ] احراز هویت API و حذف نقش از هدر `X-Role`
- [ ] endpoint فهرست production و صف بازبینی
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
- [ ] استقرار production و پایلوت پولی با provider واقعی

این نوبت پژوهش و مستندسازی بود؛ کد تغییر نکرد و build/test تازه اجرا نشد. موارد فوق به معنی اجرای قابلیت یا آمادگی فروش نیستند.

## نوبت بعد: بازبینی صداقت و provider واقعی اسکریپت — 2026-09-26

این نوبت کد تغییر کرد. ممیزی کد در برابر ادعاهای همین فایل، چند مورد `[x]` را نادرست نشان داد؛ هر مورد یا در کد اصلاح شد یا در متن مستندات صادقانه شد.

- باگ واقعی validation وزن‌ها رفع شد: مسیر startup از `IOptions` بی‌اعتبار می‌خواند، پس وزن با غلط املایی بی‌سروصدا defaults را نگه می‌داشت. تست تازه خود composition root را می‌آزماید، چون تست قبلی فقط extension را می‌آزمود و دقیقاً همین شکاف را از کسور گذاشته بود.
- allowlist مربوط به redirect اصلاح شد؛ پیش از این، یک feed مجاز می‌توانست به هر میزبانی برود.
- `OpenAiCompatibleScriptGenerator` اضافه شد و claimهای دارای `sourceId` ناشناخته را رد می‌کند. `ScriptGenerationFailedException` با ۵۰۲ از ۴۰۹ِ draft رد‌شده جدا شد.
- `ITextToSpeechProvider` که در DI ثبت نشده بود ثبت شد، ولی provider واقعی هنوز نیست.

**آنچه اجرا نشد:** `CreatorRizz.Persistence.Tests` در این نوبت اجرا نشد. دسترسی به registry داکر denied بود، PostgreSQL بالا نیامد و هر ۳۲ تست integration شکست خوردند. این شکست محیطی است، نه تأیید سلامت آن‌ها. `dotnet build` بدون warning، ۵۷ تست `CreatorRizz.Domain.Tests`، و در وب `npm ci`، `npm run typecheck` و `npm run build` همگی سبز بودند.

آخرین به‌روزرسانی: 2026-09-26
