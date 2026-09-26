# Domain Design

## ساختار

- `Models/` مدل‌های هسته‌ای Candidate، Production، Asset و Governance را بدون وابستگی بیرونی نگه می‌دارد.
- `Policies/` قواعد rights، research و publishing را در یک مالک روشن جمع می‌کند.
- `Workflow/` تنها مرجع transitionهای state machine و enumهای lifecycle است.
- `Rendering/` قرارداد render و اعتبارسنجی timeline را نگه می‌دارد.
- `Captions/` invariantهای caption و تولید SRT را جدا از render نگه می‌دارد.
- `Scoring/` مسئول محاسبه ViralScore است.
- `Scripting/` تنها مسئول ساخت draft مبتنی بر research معتبر است.

## اصول اعمال‌شده

- **SRP:** هر فایل یک مدل یا یک policy/workflow مشخص دارد؛ فایل تجمیعی entityها حذف شده است.
- **OCP:** policyها و state machine از callerها جدا هستند و قانون جدید بدون تکرار در API یا storage اضافه می‌شود.
- **LSP/ISP:** interface اضافی برای مدل‌های pure domain ایجاد نشده است؛ هیچ زیرنوع یا abstraction تزئینی وجود ندارد.
- **DIP:** Domain به API، Infrastructure، provider، پایگاه‌داده یا framework وابسته نیست. وابستگی‌های خارجی در لایه‌های بیرونی باقی می‌مانند.
