# نقشه اجرای CreatorRizz

هدف این بسته، ساخت CreatorRizz برای تولید حدود 3 YouTube Short در روز است. سیستم باید از discovery تا انتشار private را پشتیبانی کند؛ انتشار عمومی تنها پس از تایید انسانی انجام می‌شود.

این برنامه برای یک مخزن تازه نوشته شده است. هنوز مخزن، revision یا commandهای واقعی CI وجود ندارد. Plan 001 باید آن‌ها را بسازد و هر executor باید پیش از شروع plan بعدی، README و pipeline فعلی را بخواند.

## ترتیب اجرا و وضعیت

| Plan | عنوان | اندازه | وابستگی | وضعیت |
|---|---|---|---|---|
| [001](001-bootstrap.md) | Bootstrap و قراردادهای اجرایی | M | - | IN PROGRESS  CI is manual-only; .NET build/test passed; web dependency install remains an environment gate |
| [002](002-domain-persistence.md) | مدل دامنه و persistence | M | 001 | DONE  PostgreSQL تنها مسیر persistence؛ EF Core مالک schema؛ تست integration روی دیتابیس واقعی |
| [003](003-workflow-reviews-api.md) | state machine و API بازبینی | M | 002 | DONE  گیت‌های کامل تا PublishApproved، optimistic concurrency با If-Match/ETag، تست integration روی PostgreSQL |
| [004](004-discovery-research-script.md) | Discovery، research و script | L | 002، 003 | DONE  fingerprint dedupe، ViralScore v1 configurable، research pack ≥2 sources، DisabledScriptGenerator، worker timer-based، migration با backfill SQL |
| [005](005-assets-rights.md) | registry دارایی و گیت حقوق | M | 003 | IN PROGRESS  Rights policy and local asset attachment added; object storage remains pending |
| [006](006-voice-render-subtitles.md) | TTS، FFmpeg و subtitle | L | 004، 005 | IN PROGRESS  Render manifest and FFmpeg preflight adapter added; FFmpeg binary and media storage are not available |
| [007](007-react-operations-ui.md) | داشبورد React و صف عملیاتی | L | 003 تا 006 | TODO |
| [008](008-youtube-publishing.md) | OAuth و انتشار private در YouTube | M | 006، 007 | TODO |
| [009](009-observability-security.md) | امنیت، رصد و recovery | M | 001 تا 008 | TODO |
| [010](010-pilot-hardening.md) | پایلوت 3 Short در روز و hardening | M | 001 تا 009 | TODO |

## قواعد تحویل بین AIها

1. هر AI فقط یک plan را اجرا کند و خارج از scope آن تغییر ندهد.
2. قبل از کدنویسی، drift check plan و وضعیت git را اجرا کند.
3. قبل از تحویل، تست‌ها را اجرا و نتیجه را در PR یا handback بنویسد.
4. اگر تصمیم معماری تازه لازم شد، AI باید stop کند و handback بنویسد: وضعیت فعلی، گزینه‌ها، فایل‌ها و سوال باز. تصمیم را بداهه نگیرد.
5. هیچ secret، OAuth token یا فایل media خصوصی commit نشود.

## چک لیست اجرای قابل مشاهده

- [x] ✅ monorepo، Docker Compose، CI و health endpoint
- [x] ✅ نصب و build شدن وابستگی‌های React با npm (lockfile و typeها اضافه شد)
- [x] ✅ state machine برای Script، Rights و Publish
- [x] ✅ transition کامل تا PublishApproved بدون دور زدن گیت
- [x] ✅ optimistic concurrency با `If-Match`/`ETag` و 409 قابل فهم
- [x] ✅ گیت حقوق برای دارایی‌های Unknown و CommentaryRisk
- [x] ✅ Candidate intake، جلوگیری از تکرار و ViralScore v1
- [x] ✅ حداقل دو منبع قابل استفاده برای research pack و claim-map اجباری برای script
- [x] ✅ parser امن RSS برای تبدیل feed به candidate استاندارد
- [x] ✅ نسخه‌بندی script و نگهداری نسخه‌های پیشین در API محلی
- [x] ✅ ساخت draft محلی از research pack با claim map منبع‌محور
- [x] ✅ تولید استاندارد SRT از timeline caption cueها
- [x] ✅ گیت انتشار که PublishApproved و حقوق مجاز را پیش از upload الزامی می‌کند
- [x] ✅ correlation ID و audit trail محلی برای Production
- [x] ✅ security headerها و rate limit پایه برای API
- [x] ✅ health endpointهای جدا برای live و ready
- [x] ✅ یکدست‌سازی نام محصول به CreatorRizz در سندها و رابط اولیه
- [x] ✅ تبدیل و استفاده از لوگوی CreatorRizz در رابط وب
- [x] ✅ ثبت پالت رنگ CreatorRizz در مستندات و tokenهای رابط وب
- [x] ✅ PostgreSQL، Redis، object storage و migration واقعی
- [x] ✅ schema اولیه PostgreSQL برای workflow و audit events
- [x] ✅ EF Core به‌عنوان تنها مالک schema با migration و اجرای آن در startup
- [x] ✅ constraintهای database: canonical URL یکتا، script version یکتا، جلوگیری از AssetUsage orphan
- [x] ✅ تست integration روی PostgreSQL واقعی
- [ ] ⏳ Redis queue پایدار و object storage واقعی S3 (فعلاً local)
- [x] ✅ قرارداد object storage و پیاده‌سازی محلی امن برای asset و render artifact
- [x] ✅ صف job محلی با backpressure برای انتقال کارهای سنگین به worker
- [x] ✅ قرارداد TTS با اعتبارسنجی متن، voice و سرعت گفتار
- [x] ✅ صدای پیش‌فرض TTS برابر alloy با امکان override برای هر ویدئو
- [x] ✅ API صف TTS که فقط بعد از ScriptApproved اجرا می‌شود
- [x] ✅ API صف render که RightsApproved و manifest معتبر را الزامی می‌کند
- [x] ✅ discovery از YouTube/RSS، research worker و AI script adapter
- [ ] ⏳ TTS، FFmpeg واقعی، subtitle و preview
- [ ] ⏳ React operations dashboard
- [ ] ⏳ YouTube OAuth و private upload
- [ ] ⏳ alerting، backup/restore و پایلوت 3 Short در روز

## تصمیم‌های ثابت

- Backend: ASP.NET Core modular monolith.
- Frontend: React و TypeScript.
- PostgreSQL برای داده تراکنشی؛ Redis برای queue، cache و lock کوتاه.
- Object storage سازگار با S3 برای media و render artifact.
- FFmpeg در worker container؛ GPU و AI self-hosted خارج از دامنه MVP.
- ویدئو در MVP فقط بعد از Script Approval، Rights Approval و Publish Approval به صورت private یا scheduled ارسال می‌شود.
- همه تغییرهای بعد از Rights Approval، تایید حقوق را باطل می‌کنند.

## معیار پایان برنامه

- اپراتور بتواند از یک Candidate، research pack، script، voice، دارایی، preview و subtitle بسازد.
- بازبین بتواند روایت، حقوق و انتشار را جداگانه تایید یا رد کند.
- دارایی Unknown هیچ گاه به render یا publish نرسد.
- سیستم با یک کانال و یک اپراتور، 3 Short در روز را به صورت پایدار پشتیبانی کند.
