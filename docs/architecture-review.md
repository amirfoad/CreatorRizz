# Architecture Review

## 2026-09-26

### اصلاح‌شده

- ایجاد Production اکنون Candidate موجود را الزامی می‌کند.
- Research Pack و sourceها باید به همان Candidate متعلق باشند که برای آن script draft ساخته می‌شود.
- Publish با فهرست خالی asset متوقف می‌شود.
- title Candidate و reliability score منبع در مرز API اعتبارسنجی می‌شوند.

### کار باقی‌مانده

- storage موقت in-memory هنوز در API قرار دارد و باید در مرحله persistence به Infrastructure منتقل شود.
- صف in-memory فقط برای توسعه محلی مناسب است و در deployment باید پایدار شود.
