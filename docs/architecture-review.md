# Architecture Review

## 2026-09-26

### اصلاح‌شده

- ایجاد Production اکنون Candidate موجود را الزامی می‌کند.
- Research Pack و sourceها باید به همان Candidate متعلق باشند که برای آن script draft ساخته می‌شود.
- Publish با فهرست خالی asset متوقف می‌شود.
- title Candidate و reliability score منبع در مرز API اعتبارسنجی می‌شوند.
- persistence موقت Candidate و Production از API به `Infrastructure/Persistence` منتقل شد؛ API اکنون فقط DTOهای HTTP را به adapter توسعه‌ای نگاشت می‌کند.
- `Program.cs` به composition root کوچک تبدیل شد؛ endpointهای Candidate و Production و قراردادهای HTTP هر کدام مرز پوشه‌ای روشن دارند.
- middlewareهای cross-cutting شامل correlation ID و security header در `Api/Middleware` متمرکز شدند.
- پیکربندی Swagger در `Api/OpenApi` قرار گرفت و فقط در محیط Development فعال است.
- `Domain/Entities.cs` به مدل‌های تک‌مسئولیتی تقسیم شد؛ caption validation نیز از render manifest جدا شد تا SRT فقط به invariantهای caption وابسته باشد.
- لایه `CreatorRizz.Application` افزوده شد؛ API دیگر مستقیماً به store یا queue Infrastructure وابسته نیست و workflow را از use case مرکزی دریافت می‌کند.
- adapterهای Infrastructure از فایل‌های ریشه به پوشه‌های concern-based منتقل شدند؛ composition و options نیز جداگانه نگه‌داری می‌شوند.

### کار باقی‌مانده

- storage موقت همچنان in-memory و فقط مناسب توسعه محلی است؛ در مرحله persistence باید با PostgreSQL جایگزین شود.
- صف in-memory فقط برای توسعه محلی مناسب است و در deployment باید پایدار شود.
