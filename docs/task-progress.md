# CreatorRizz Progress

این فایل، وضعیت بخش‌هایی را ثبت می‌کند که در کد پروژه انجام و بررسی شده‌اند.

- [x] پایه ASP.NET Core، worker، Docker Compose و health checks
- [x] state machine و گیت‌های بازبینی Script، Rights و Publish
- [x] Candidate intake، جلوگیری از تکرار و ViralScore v1
- [x] Research Pack، claim map و versioning اسکریپت
- [x] policy حقوق asset و جلوگیری از render دارایی نامشخص
- [x] manifest، SRT و صف‌های TTS و render
- [x] object storage محلی امن و schema اولیه PostgreSQL
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
- [~] اتصال EF Core و Npgsql به PostgreSQL — DbContext و schema آماده است؛ repositoryهای workflow هنوز in-memory هستند
- [ ] provider واقعی AI، TTS و FFmpeg
- [ ] React operations dashboard
- [ ] OAuth و upload private به YouTube
- [ ] پایلوت 3 Short در روز

آخرین به‌روزرسانی: 2026-09-26
