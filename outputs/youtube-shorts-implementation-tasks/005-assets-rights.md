# Plan 005: registry دارایی و گیت حقوق

## وابستگی

Plan 003 باید DONE باشد.

## هدف

هر فایل ویدئو، تصویر، موسیقی یا SFX باید پیش از رندر، منبع، وضعیت حقوق و دلیل روایی داشته باشد. Unknown و Rejected نباید به render queue برسند.

## Scope

- upload/object-storage adapter
- Asset و AssetUsage API
- evidence metadata و rights validation
- testهای policy

## مراحل

1. upload را با signed URL یا server-mediated upload بسازید؛ object key نباید مستقیم از input کاربر بیاید.
2. برای هر Asset: type، source URL، duration، license evidence، rights status و checksum ذخیره کنید.
3. برای هر AssetUsage: in/out timestamp، narrative purpose و linked claim/source نگه دارید.
4. validator مرکزی بسازید که فقط Owned، Licensed، PermissionGranted و وضعیت‌های تاییدشده internal را مجاز کند. CommentaryRisk نیازمند RightsApproval صریح است.
5. attribution metadata را در production نگه دارید تا در description یا internal record استفاده شود.

## پذیرش و verification

- Unknown و Rejected با API یا worker وارد render نمی‌شوند.
- تغییر asset یا timing بعد از RightsApproved، decision را invalid می‌کند.
- فایل تکراری با checksum یکسان دوباره object storage را پر نمی‌کند.

## STOP conditions

- اگر provider storage فاقد signed URL یا lifecycle policy است، قبل از workaround، handback بنویسید.

## تحویل به AI بعدی

rights matrix و فرمت evidence قابل قبول را در docs ثبت کنید.
