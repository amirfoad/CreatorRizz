# Plan 004: Discovery، research و تولید script منبع‌محور

## وابستگی

Planهای 002 و 003 باید DONE باشند.

## هدف

سیستم از sourceهای مجاز Candidate می‌سازد، آن‌ها را رتبه‌بندی می‌کند و فقط بر مبنای source itemهای ذخیره‌شده، research pack و script draft تولید می‌کند.

## Scope

- worker discovery و provider interface
- ViralScore v1
- research pack، claim map و script generation adapter
- retry، dedupe و testهای deterministic

## مراحل

1. یک `IDiscoverySource` interface بسازید و MVP را با YouTube Data API و RSS/mock provider آغاز کنید. از scraping یا دانلود محتوا خارج از scope خودداری کنید.
2. Candidate را با canonical URL و fingerprint deduplicate کنید.
3. ViralScore v1 را با وزن‌های configurable اجرا کنید: view velocity 35، engagement 20، recency 15، creator relevance 10، cross-source 10، story potential 10.
4. research worker باید source itemها را ذخیره و fact/uncertainty را جدا کند. اگر منابع کافی نیستند، production نسازید.
5. AI adapter باید prompt version، model id، input references و output را ذخیره کند. Script claim map باید هر claim را به SourceItem وصل کند.

## پذیرش و verification

- یک provider fake، Candidateهای تکراری را تنها یک بار ثبت می‌کند.
- ViralScore با ورودی ثابت deterministic است.
- script draft بدون ResearchPack یا claim map وارد review نمی‌شود.
- job retry یک Candidate duplicate یا script version تکراری تولید نمی‌کند.

## STOP conditions

- اگر API quota یا Terms برای source موردنظر روشن نیست، آن provider را فعال نکنید و در handback فقط source مجاز پیشنهادی را ذکر کنید.
- اگر مدل AI منبع را نمی‌تواند حمل کند، production را با citation جعلی ادامه ندهید.

## تحویل به AI بعدی

providerهای فعال، token/cost guardrail و schema claim map را مستند کنید.
