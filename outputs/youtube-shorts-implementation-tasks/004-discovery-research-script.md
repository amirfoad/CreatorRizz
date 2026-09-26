# Plan 004: Discovery، research و تولید script منبع‌محور

## وابستگی

Planهای 002 و 003 باید DONE باشند.

## وضعیت: DONE

## هدف

سیستم از sourceهای مجاز Candidate می‌سازد، آن‌ها را رتبه‌بندی می‌کند و فقط بر مبنای source itemهای ذخیره‌شده، research pack و script draft تولید می‌کند.

## Scope

- worker discovery و provider interface
- ViralScore v1
- research pack، claim map و script generation adapter
- retry، dedupe و testهای deterministic

## مراحل

1. یک `IDiscoverySource` interface بسازید و MVP را با RSS/mock provider آغاز کنید. از scraping یا دانلود محتوا خارج از scope خودداری کنید. YouTube فعلاً فعال نیست چون quota و Terms تأیید نشده.
2. Candidate را با canonical URL و fingerprint SHA-256 نرمال‌شدهٔ title|creator deduplicate کنید. ردیف‌های قدیمی collision دار با id خودشان salted می‌شوند و هیچ داده‌ای از بین نمی‌رود.
3. ViralScore v1 را با وزن‌های configurable اجرا کنید: view velocity 35، engagement 20، recency 15، creator relevance 10، cross-source 10، story potential 10. مجموع باید دقیقاً ۱۰۰ باشد و نام ناشناختهٔ weight در startup صدا می‌زند.
4. research worker باید source itemها را ذخیره و fact/uncertainty را جدا کند. حداقل دو منبع با reliability ≥ 50 و excerpt لازم است؛ در غیر این صورت pack ساخته نمی‌شود.
5. AI adapter (`IScriptGenerator`) validate می‌کند و خطا می‌دهد. `DisabledScriptGenerator` بدون مدل و بدون API key، production را با ۴۰۹ رد می‌کند. `ScriptGeneration` با `ScriptVersion` در یک write اتمی ثبت می‌شود.

## پذیرش و verification

- یک provider fake، Candidateهای تکراری را تنها یک بار ثبت می‌کند؛ داستان قدیمی با URL جدید و نگارش متفاوت همون candidate را برمی‌گرداند.
- ViralScore با ورودی ثابت deterministic است؛ backfill SQL همان hash domain را تولید می‌کند (تست `FingerprintBackfillTests` شامل فارسی).
- script draft بدون ResearchPack یا claim map وارد review نمی‌شود.
- بدون `If-Match`، draft ۴۲۸ می‌دهد؛ با disabled generator، ۴۰۹ و بدون هیچ تغییری در version.
- worker retry هیچ duplicate یا version تکراری تولید نمی‌کند.

## STOP conditions

- اگر API quota یا Terms برای source موردنظر روشن نیست، آن provider را فعال نکنید و در handback فقط source مجاز پیشنهادی را ذکر کنید.
- اگر مدل AI منبع را نمی‌تواند حمل کند، production را با citation جعلی ادامه ندهید.

## تحویل به AI بعدی

providerهای فعال، token/cost guardrail و schema claim map را مستند کنید.
