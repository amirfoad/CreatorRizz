# Plan 001: ایجاد مخزن و پایه اجرایی

## هدف

یک monorepo قابل اجرا بسازید که backend، worker و frontend را جدا نگه می‌دارد، اما برای MVP ساده deploy می‌شود. در پایان این plan، commandهای واقعی build، test، lint و typecheck باید در README ثبت و در CI اجرا شوند.

## Scope

- `src/Shorts.Api/`، `src/Shorts.Workers/`، `src/Shorts.Domain/`، `src/Shorts.Infrastructure/`
- `web/`
- `docker-compose.yml`، `.env.example`، `.github/workflows/ci.yml`، `README.md`

## خارج از scope

- مدل دامنه، API محصول، OAuth، FFmpeg و UI واقعی.

## مراحل

1. یک git repository جدید با solution .NET و یک React TypeScript app بسازید. backend باید health endpoint داشته باشد و frontend باید آن را در development نشان دهد.
2. Docker Compose محلی برای PostgreSQL، Redis و یک object-storage سازگار با S3 اضافه کنید. credentialهای نمونه فقط در `.env.example` باشند.
3. قرارداد configuration را بسازید: Database، Redis، ObjectStorage، AI، TTS، YouTube. در production باید missing secret باعث startup failure روشن شود.
4. CI را برای restore، build، test، lint و typecheck بسازید. commandهای واقعی را در README ثبت کنید؛ planهای بعدی فقط همین commandها را به کار می‌برند.
5. `.gitignore` برای secrets، media، render output و local storage اضافه کنید.

## پذیرش و verification

- `docker compose up -d` سرویس‌های محلی را healthy نشان می‌دهد.
- commandهای ثبت‌شده در README برای backend و web با exit code صفر تمام می‌شوند.
- `GET /health` پاسخ success می‌دهد.
- CI بدون secret واقعی کار می‌کند.

## STOP conditions

- اگر انتخاب template یا hosting stack به ابزار متفاوتی نیاز دارد، فقط یک گزینه پیشنهادی با دلیل کوتاه در handback بنویسید.
- اگر .NET یا Node runtime در محیط هدف موجود نیست، نسخه لازم را مشخص کنید و ادامه ندهید.

## تحویل به AI بعدی

نام و مسیر commandهای واقعی build/test/lint/typecheck، نسخه runtimeها، و compose service names را در README اضافه کنید.
