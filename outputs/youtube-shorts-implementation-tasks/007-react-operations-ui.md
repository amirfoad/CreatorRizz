# Plan 007: React dashboard و صف بازبینی

## وابستگی

Planهای 003 تا 006 باید DONE باشند.

## هدف

اپراتور باید بدون رجوع به database یا log بتواند Candidateها را ببیند، research و script را بازبینی کند، دارایی را به timeline وصل کند و preview را تایید یا برای اصلاح برگرداند.

## Scope

- React routes، API client و auth integration
- Candidate queue، Production detail، review panels، asset picker، preview player
- loading/error/empty states و accessibility پایه

## مراحل

1. Candidate queue با score، age، state، risk و filter بسازید.
2. صفحه Production detail باید tabs یا بخش‌های Research، Script، Assets، Preview و Audit داشته باشد. source URL و uncertainty را قابل مشاهده نگه دارید.
3. هر review action باید decision، note و API conflict را روشن نمایش دهد.
4. asset picker باید rights status را پیش از attach نشان دهد و Unknown/Rejected را غیرقابل انتخاب کند.
5. preview شامل player، subtitle toggle، render manifest summary و دکمه‌های rework/approve باشد.

## پذیرش و verification

- کاربر می‌تواند production از ScriptDraft تا PublishReview را در UI بررسی کند.
- responseهای 409/concurrency و transition error قابل فهم‌اند.
- keyboard navigation و label کنترل‌های review کار می‌کند.
- frontend test و typecheck commandهای واقعی سبز باشند.

## STOP conditions

- اگر backend contract مبهم است، mock قرارداد تازه نسازید؛ نمونه request/response موردنیاز را handback کنید.

## تحویل به AI بعدی

مسیرها، نقش‌های لازم و screenshot یا storybook stateهای مهم را در docs ثبت کنید.
