# Plan 009: رصد، امنیت و recovery

## وابستگی

Planهای 001 تا 008 باید DONE باشند.

## هدف

وقتی job شکست می‌خورد یا token/quota به مشکل می‌خورد، تیم باید در چند دقیقه علت، production متاثر و اقدام بعدی را بداند؛ بدون اینکه secret یا media خصوصی افشا شود.

## Scope

- structured logging، correlation id و metrics
- health/readiness checks و alert rules
- secret policy، RBAC audit و data retention
- backup/restore test و incident runbook

## مراحل

1. correlation ID را از API command تا worker job، render و publish propagation دهید.
2. metrics برای queue depth، job duration، retry، render failure، API quota و workflow funnel اضافه کنید.
3. health و readiness checkها باید database، Redis و object storage dependency را جدا نشان دهند.
4. secret redaction test و RBAC audit test بنویسید.
5. lifecycle policy برای raw media، preview و published artifacts و backup PostgreSQL تعریف کنید.

## پذیرش و verification

- از یک production id، logs و audit events کل lifecycle پیدا می‌شوند.
- secret test ثابت می‌کند token در log/error response نیست.
- restore procedure روی database test با نتیجه قابل ثبت اجرا می‌شود.
- alert fixture یک render failure threshold را trigger می‌کند.

## STOP conditions

- اگر observability vendor انتخاب نشده، abstraction بیش از حد نسازید؛ OpenTelemetry پایه و handback انتخاب vendor کافی است.

## تحویل به AI بعدی

dashboard metric definitions، retention days و incident runbook را مستند کنید.
