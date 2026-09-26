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
- `src/CreatorRizz.Infrastructure`: configuration، adapterهای بیرونی و persistence توسعه‌ای (`Persistence/`)
- `src/CreatorRizz.Workers`: workerهای پس زمینه
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

PostgreSQL schema در `db/init/001_schema.sql` قرار دارد و هنگام ساخت volume تازه توسط Docker Compose اجرا می‌شود.

`CreatorRizzDbContext` در `src/CreatorRizz.Infrastructure/Persistence` مدل‌های اصلی را به PostgreSQL نگاشت می‌کند. در وضعیت فعلی repositoryهای workflow هنوز adapterهای in-memory هستند؛ جایگزینی آن‌ها با repositoryهای EF Core مرحلهٔ بعدی است.

Discovery اولیه RSS در `src/CreatorRizz.Infrastructure/RssDiscovery.cs` قرار دارد. قبل از اتصال feedهای واقعی، آن‌ها باید در allowlist عملیاتی پروژه ثبت شوند.

تا زمان اتصال PostgreSQL، داده‌های Candidate و Production در `InMemoryCandidateStore` و `InMemoryProductionStore` نگه‌داری می‌شوند؛ با توقف API حذف خواهند شد. این adapterها عمداً خارج از لایه API و زیر `src/CreatorRizz.Infrastructure/Persistence` قرار دارند.

در API، `Program.cs` فقط composition root است. قراردادهای HTTP در `Contracts/`، endpointهای هر جریان در `Endpoints/`، middlewareها در `Middleware/` و ترجمهٔ خطاهای موردانتظار در `Results/` نگه‌داری می‌شوند.

وابستگی backend به سمت داخل است: `Domain ← Application ← Infrastructure/API`. API فقط درخواست HTTP را به use caseهای Application می‌سپارد و Infrastructure، portهای Application را پیاده‌سازی می‌کند.

Infrastructure بر اساس نوع adapter دسته‌بندی شده است: `Configuration/`، `DependencyInjection/`، `Persistence/`، `Jobs/`، `Storage/`، `Discovery/`، `Tts/` و `Publishing/`.
