# CreatorRizz

CreatorRizz ابزار داخلی برای کشف سوژه، تولید روایت، بازبینی حقوق، رندر و انتشار کنترل‌شده YouTube Shorts است.

## ساختار

- `src/Shorts.Api`: HTTP API و health endpoint
- `src/Shorts.Domain`: مدل و ruleهای دامنه
- `src/Shorts.Infrastructure`: configuration و adapterهای بیرونی
- `src/Shorts.Workers`: workerهای پس زمینه
- `web`: React operations dashboard

## توسعه محلی

1. یک کپی از `.env.example` با نام `.env` بسازید و passwordهای محلی را تغییر دهید.
2. `docker compose up -d`
3. `dotnet restore ShortsAutomation.sln`
4. `dotnet build ShortsAutomation.sln --configuration Release`
5. `dotnet test ShortsAutomation.sln --configuration Release`
6. از پوشه `web`: `npm ci`، سپس `npm run typecheck` و `npm run build`

API در development با `dotnet run --project src/Shorts.Api` اجرا می‌شود و health check آن `GET /health` است.
