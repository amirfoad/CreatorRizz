# Plan 010: پایلوت عملیاتی و hardening برای 3 Short در روز

## وابستگی

Planهای 001 تا 009 باید DONE باشند.

## هدف

پیش از اتکا به سیستم برای کانال، یک پایلوت واقعی با حداقل 15 تا 21 Short اجرا شود. خروجی این plan فقط کد نیست؛ معیارها، failureها و SOP باید ثابت شوند.

## Scope

- test suite end-to-end با provider fake و staging
- KPI dashboard و export
- SOP اپراتور و بازبین
- defect fixes کوچک ناشی از پایلوت

## مراحل

1. یک happy path end-to-end بسازید: candidate تا private upload با fake provider و fixture asset مجاز.
2. سناریوهای ریسک را test کنید: source ناکافی، Unknown asset، change پس از RightsApproval، TTS failure، FFmpeg failure، token revoked و duplicate publish retry.
3. در staging، 15 تا 21 production واقعی را با review انسانی انجام دهید. برای هر کدام زمان stage، rework reason و outcome را ثبت کنید.
4. SOP کوتاه بنویسید: انتخاب سوژه، تایید script، تایید حقوق، publish private، recovery failure و kill switch.
5. فقط defectهایی را رفع کنید که مانع 3 Short در روز هستند. قابلیت‌های SaaS، چند کانال و A/B testing را وارد scope نکنید.

## پذیرش و verification

- حداقل 3 روز متوالی، 3 Short در روز تا private upload پیش می‌رود.
- هیچ Production با Unknown asset یا بدون approvals به upload نمی‌رسد.
- dashboard زمان میانه candidate تا preview و زمان بازبینی را نشان می‌دهد.
- SOP توسط فردی غیر از سازنده plan یک بار اجرا و ابهام‌هایش ثبت می‌شود.

## STOP conditions

- اگر claim، strike یا وضعیت حقوقی مبهم رخ داد، انتشار را متوقف کنید؛ آن رخداد را با source/evidence و بدون اقدام حقوقی خودسرانه handback کنید.
- اگر زمان انسانی از هدف 75 دقیقه در روز بیشتر است، قابلیت تازه اضافه نکنید؛ گلوگاه را اندازه‌گیری و گزارش کنید.

## تحویل نهایی

README index را با status واقعی planها، KPI پایلوت، لینک SOP و backlog deferred به‌روز کنید.
