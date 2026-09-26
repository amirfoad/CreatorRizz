# Architecture Review

## 2026-09-26

### اصلاح‌شده

- ایجاد Production اکنون Candidate موجود را الزامی می‌کند.
- Research Pack و sourceها باید به همان Candidate متعلق باشند که برای آن script draft ساخته می‌شود.
- Publish با فهرست خالی asset متوقف می‌شود.
- title Candidate و reliability score منبع در مرز API اعتبارسنجی می‌شوند.
- persistence موقت Candidate و Production از API به `Infrastructure/Persistence` منتقل شد؛ API اکنون فقط DTOهای HTTP را به adapter توسعه‌ای نگاشت می‌کند.

### کار باقی‌مانده

- storage موقت همچنان in-memory و فقط مناسب توسعه محلی است؛ در مرحله persistence باید با PostgreSQL جایگزین شود.
- صف in-memory فقط برای توسعه محلی مناسب است و در deployment باید پایدار شود.
