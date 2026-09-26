# Plan 006: TTS، FFmpeg و تولید preview

## وابستگی

Planهای 004 و 005 باید DONE باشند.

## هدف

یک Production تاییدشده باید voiceover، timeline، subtitle و preview MP4 عمودی بسازد. هر render job isolated، retry-safe و قابل ردیابی است.

## Scope

- `ITtsProvider` و adapter managed TTS
- timeline manifest
- FFmpeg worker container
- subtitle generation و preview artifacts

## مراحل

1. TTS provider interface شامل voice id، speed، request id و word/sentence timing بسازید. API key فقط از configuration امن خوانده شود.
2. timeline manifest JSON بسازید: canvas 1080x1920، audio track، clips، overlays، caption cues و music track اختیاری.
3. FFmpeg command را از manifest تولید کنید، نه از text input. temp directory را به job id محدود کنید.
4. SRT و burned-in captions را با safe escaping و آزمون فارسی/انگلیسی تولید کنید؛ خروجی MVP انگلیسی است اما ورودی نام‌های Unicode نباید render را بشکند.
5. render success تنها وقتی ثبت شود که MP4، SRT و manifest وجود و duration قابل قبول داشته باشند.

## پذیرش و verification

- fixture با voice mock و asset مجاز یک MP4 1080x1920 و SRT می‌سازد.
- asset نامجاز قبل از اجرای FFmpeg reject می‌شود.
- retry job یک output key final را overwrite نمی‌کند؛ version جدید یا idempotent reuse دارد.
- failure FFmpeg log و command sanitized را به job record اضافه می‌کند، بدون leak secret.

## STOP conditions

- اگر FFmpeg filter موردنیاز در image انتخابی موجود نیست، image را بدون بررسی license تغییر ندهید؛ handback کنید.
- اگر TTS timing API ندارد، fallback sentence timing را پیشنهاد دهید اما قبل از پیاده‌سازی تایید بگیرید.

## تحویل به AI بعدی

templateهای render، محدودیت media format و commandهای fixture render را مستند کنید.
