# Plan 008: اتصال امن به YouTube و انتشار private

## وابستگی

Planهای 006 و 007 باید DONE باشند.

## هدف

Production فقط پس از PublishApproved بتواند با OAuth کانال متصل، ابتدا به صورت private upload شود. پاسخ API، quota cost و YouTube video ID باید ثبت شوند.

## Scope

- OAuth connection lifecycle
- YouTube Data API adapter
- upload، schedule و sync status
- quota و failure handling

## مراحل

1. connect/disconnect lifecycle را بسازید؛ refresh token را encrypted ذخیره و در log redaction کنید.
2. publish command را به state machine متصل کنید. worker قبل از upload، production state، rights approval و final render checksum را دوباره بررسی کند.
3. MVP default را private قرار دهید. scheduling فقط با PublishApproval جدا و زمان UTC فعال شود.
4. response API، video ID، privacy status، failure reason و quota usage را ثبت کنید.
5. retry باید idempotency key داشته باشد تا یک ویدئو دو بار upload نشود.

## پذیرش و verification

- با fake YouTube client، upload فقط از PublishApproved شروع می‌شود.
- revoked OAuth connection پیام قابل اقدام می‌دهد و token در output نشان داده نمی‌شود.
- retry یک PublishJob، video ID قبلی را reuse یا safely reconcile می‌کند.

## STOP conditions

- اگر channel ownership یا OAuth scopes برای محیط واقعی مشخص نیست، upload واقعی انجام ندهید و scope list پیشنهادی را handback کنید.

## تحویل به AI بعدی

runbook اتصال کانال، لغو token و recovery upload را مستند کنید.
