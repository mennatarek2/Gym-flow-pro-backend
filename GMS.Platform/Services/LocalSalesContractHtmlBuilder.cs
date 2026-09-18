namespace GMS.Platform.Services;

using System.Globalization;
using System.Net;
using GMS.Platform.DTOs;

public static class LocalSalesContractHtmlBuilder
{
    public static string Build(LocalSalesContractSnapshot snap)
    {
        var ar = string.Equals(snap.Language, "ar", StringComparison.OrdinalIgnoreCase);
        var dir = ar ? "rtl" : "ltr";
        var lang = ar ? "ar" : "en";
        var L = ar ? Ar : En;
        var outstandingClass = snap.OutstandingAmount > 0 ? "due" : "";
        var statusClass = snap.PaymentStatus.ToLowerInvariant();
        var financeNote = snap.PaymentStatus.Equals("unpaid", StringComparison.OrdinalIgnoreCase) ? L.UnpaidNote
            : snap.PaymentStatus.Equals("partial", StringComparison.OrdinalIgnoreCase) ? L.PartialNote
            : L.PaidNote;

        var itemRows = string.Join("", snap.Items.Select(i => $@"
            <tr>
              <td>{E(i.Sku)}</td>
              <td>{E(i.Name)}</td>
              <td>{E(i.ProductType)}</td>
              <td class=""r"">{i.Quantity.ToString("0.##", CultureInfo.InvariantCulture)}</td>
              <td class=""r"">{Money(i.UnitPrice, snap.Currency)}</td>
              <td class=""r"">{Money(i.DiscountAmount, snap.Currency)}</td>
              <td class=""r"">{Money(i.LineTotal, snap.Currency)}</td>
            </tr>"));

        var payRows = snap.Payments.Count == 0
            ? $"<tr><td colspan=\"4\">{E(L.NoPayments)}</td></tr>"
            : string.Join("", snap.Payments.Select(p => $@"
            <tr>
              <td>{E(p.PaymentDate)}</td>
              <td>{E(Method(p.PaymentMethod, ar))}</td>
              <td>{E(p.Reference)}</td>
              <td class=""r"">{Money(p.Amount, snap.Currency)}</td>
            </tr>"));

        var gymCode = string.IsNullOrWhiteSpace(snap.Customer.GymCode)
            ? L.GymCodePending
            : snap.Customer.GymCode;

        var licenseBlock = snap.License == null
            ? $"<p class=\"notice\">{E(L.LicensePending)}</p>"
            : $@"
        <table>
          <tbody>
            <tr><td>{E(L.Edition)}</td><td>HyMotion Local · {E(snap.License.Edition)}</td></tr>
            <tr><td>{E(L.Devices)}</td><td>{snap.License.DeviceLimit}</td></tr>
            <tr><td>{E(L.LicenseRef)}</td><td><b>{E(snap.License.LicenseReference)}</b></td></tr>
            <tr><td>{E(L.LicenseIssued)}</td><td>{E(snap.License.IssuedOn)}</td></tr>
            <tr><td>{E(L.GymCode)}</td><td>{E(snap.License.GymCode ?? gymCode)}</td></tr>
          </tbody>
        </table>
        <p class=""notice"">{E(L.Masked)}</p>";

        var terms = string.Join("", SplitTerms(snap.Terms).Select((t, i) => $"<li>{E(t)}</li>"));

        return $@"<!DOCTYPE html>
<html lang=""{lang}"" dir=""{dir}"">
<head>
<meta charset=""UTF-8"">
<title>{E(L.DocTitle)} {E(snap.ContractNumber)}</title>
<style>
@page{{size:A4;margin:12mm}}
body{{margin:0;font-family:""IBM Plex Sans"",""IBM Plex Sans Arabic"",Arial,sans-serif;color:#16181d;background:#fff}}
.sheet{{width:auto;padding:4mm 2mm 8mm;page-break-after:always}}
.sheet:last-child{{page-break-after:auto}}
.hdr{{display:flex;justify-content:space-between;gap:16px;border-bottom:2px solid #16181d;padding-bottom:10px}}
.seller{{font-size:22px;font-weight:700}}
.kicker{{font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:#1f6b4a;font-weight:700}}
.title{{font-size:20px;font-weight:700;margin:4px 0 8px}}
.num{{display:inline-block;border:1px solid #16181d;padding:3px 7px;font-weight:700}}
.meta{{font-size:12px;color:#5b616c;margin:3px 0}}
.parties{{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:14px 0}}
.box{{border:1px solid #d8dbe2;padding:10px 12px}}
.box h4{{margin:0 0 6px;font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:#8a909b}}
.who{{font-weight:700;font-size:14px}}
.box p{{margin:2px 0;font-size:12px;color:#5b616c}}
.section{{margin:14px 0 0}}
.section h3{{margin:0 0 8px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;border-bottom:1px solid #d8dbe2;padding-bottom:4px}}
table{{width:100%;border-collapse:collapse;font-size:12px}}
th{{text-align:inherit;font-size:10px;text-transform:uppercase;border-bottom:1px solid #16181d;padding:6px 0}}
td{{padding:7px 0;border-bottom:1px solid #d8dbe2;vertical-align:top}}
.r{{text-align:right}}
html[dir=rtl] .r{{text-align:left}}
.finance{{display:grid;grid-template-columns:1.2fr .8fr;gap:16px}}
.totals{{border:1px solid #16181d;padding:10px 12px}}
.tot{{display:flex;justify-content:space-between;gap:12px;margin:4px 0;font-size:13px}}
.tot.grand{{font-size:16px;font-weight:700;border-top:1px solid #16181d;padding-top:8px;margin-top:8px}}
.tot.due{{font-weight:700}}
.badge{{display:inline-block;font-size:10px;font-weight:700;text-transform:uppercase;padding:3px 7px;border-radius:999px}}
.badge.paid{{background:#dcfce7;color:#14532d}}
.badge.partial{{background:#fef3c7;color:#7c4a03}}
.badge.unpaid{{background:#fee2e2;color:#7f1d1d}}
.notice{{font-size:11px;color:#5b616c;line-height:1.45;margin-top:8px}}
.notice.warn{{color:#8a4b00;background:#fff8e8;border:1px solid #ead7a3;padding:8px 10px}}
.signs{{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin-top:28px}}
.sign{{border-top:1px solid #16181d;padding-top:8px;min-height:64px;font-size:12px}}
.role{{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:#8a909b}}
ol{{margin:0;padding-inline-start:18px}}
li{{margin:0 0 8px;font-size:12px;line-height:1.45}}
.legal{{font-size:10px;color:#8a909b;margin-top:14px;line-height:1.4}}
.foot{{margin-top:18px;padding-top:8px;border-top:1px solid #d8dbe2;display:flex;justify-content:space-between;font-size:10px;color:#8a909b}}
.money{{unicode-bidi:isolate;direction:ltr}}
</style>
</head>
<body>
<article class=""sheet"">
  <header class=""hdr"">
    <div>
      <div class=""seller"">HyMotion</div>
      <div class=""meta"">{E(L.SellerNote)}</div>
    </div>
    <div>
      <div class=""kicker"">{E(L.Kicker)}</div>
      <div class=""title"">{E(L.DocTitle)}</div>
      <div class=""meta"">{E(L.ContractNo)}: <span class=""num"">{E(snap.ContractNumber)}</span></div>
      <div class=""meta"">{E(L.Issued)}: <b>{E(snap.IssuedOn)}</b></div>
      <div class=""meta"">{E(L.Reprint)}</div>
    </div>
  </header>
  <div class=""parties"">
    <div class=""box"">
      <h4>{E(L.Seller)}</h4>
      <div class=""who"">{E(snap.SellerName)}</div>
    </div>
    <div class=""box"">
      <h4>{E(L.Customer)}</h4>
      <div class=""who"">{E(snap.Customer.BusinessName)}</div>
      <p>{E(L.Owner)}: {E(snap.Customer.OwnerName)}</p>
      <p>{E(snap.Customer.Phone)}</p>
      <p>{E(snap.Customer.Email)}</p>
      <p>{E(snap.Customer.Address)}</p>
      <p>{E(L.GymCode)}: {E(gymCode)}</p>
    </div>
  </div>
  <section class=""section"">
    <h3>{E(L.Product)}</h3>
    <table>
      <thead><tr><th>{E(L.Sku)}</th><th>{E(L.Item)}</th><th>{E(L.Type)}</th><th class=""r"">{E(L.Qty)}</th><th class=""r"">{E(L.Unit)}</th><th class=""r"">{E(L.Disc)}</th><th class=""r"">{E(L.Line)}</th></tr></thead>
      <tbody>{itemRows}</tbody>
    </table>
  </section>
  <section class=""section finance"">
    <div>
      <h3>{E(L.Payments)}</h3>
      <table>
        <thead><tr><th>{E(L.Date)}</th><th>{E(L.Method)}</th><th>{E(L.Ref)}</th><th class=""r"">{E(L.Paid)}</th></tr></thead>
        <tbody>{payRows}</tbody>
      </table>
    </div>
    <div>
      <h3>{E(L.Finance)}</h3>
      <div class=""totals"">
        <div class=""tot""><span>{E(L.Original)}</span><span class=""money"">{Money(snap.Subtotal, snap.Currency)}</span></div>
        <div class=""tot""><span>{E(L.Discount)}</span><span class=""money"">{Money(snap.Discount, snap.Currency)}</span></div>
        <div class=""tot grand""><span>{E(L.Final)}</span><span class=""money"">{Money(snap.Total, snap.Currency)}</span></div>
        <div class=""tot""><span>{E(L.Paid)}</span><span class=""money"">{Money(snap.PaidAmount, snap.Currency)}</span></div>
        <div class=""tot {outstandingClass}""><span>{E(L.Outstanding)}</span><span class=""money"">{Money(snap.OutstandingAmount, snap.Currency)}</span></div>
        <div class=""tot""><span>{E(L.Status)}</span><span class=""badge {E(statusClass)}"">{E(Status(snap.PaymentStatus, ar))}</span></div>
      </div>
      <div class=""notice{(snap.OutstandingAmount > 0 ? " warn" : "")}"">{E(financeNote)} {E(L.SnapshotNote)}</div>
    </div>
  </section>
  <section class=""section"">
    <h3>{E(L.License)}</h3>
    {licenseBlock}
  </section>
  <p class=""notice"">{E(L.NotTax)} {E(L.NotMember)}</p>
  <div class=""signs"">
    <div class=""sign""><div class=""role"">{E(L.SignSeller)}</div>{E(L.SignLine)}</div>
    <div class=""sign""><div class=""role"">{E(L.SignCustomer)}</div>{E(L.SignLine)}</div>
  </div>
  <footer class=""foot""><span>{E(L.Foot)}</span><span>{E(L.Page1)}</span></footer>
</article>
<article class=""sheet"">
  <header class=""hdr"">
    <div>
      <div class=""kicker"">{E(L.Kicker)}</div>
      <div class=""title"">{E(L.TermsTitle)}</div>
      <div class=""meta"">{E(L.ContractNo)}: <b>{E(snap.ContractNumber)}</b> · {E(L.Issued)}: {E(snap.IssuedOn)}</div>
    </div>
  </header>
  <p class=""notice"">{E(L.TermsIntro)}</p>
  <ol>{terms}</ol>
  <section class=""section"">
    <h3>{E(L.Ack)}</h3>
    <p>{E(L.AckText)}</p>
  </section>
  <div class=""signs"">
    <div class=""sign""><div class=""role"">{E(L.SignSeller)}</div>{E(L.SignLine)}</div>
    <div class=""sign""><div class=""role"">{E(L.SignCustomer)}</div>{E(L.SignLine)}</div>
  </div>
  <p class=""legal"">{E(L.Legal)}</p>
  <footer class=""foot""><span>{E(L.Foot)} · {E(L.Reprint)}</span><span>{E(L.Page2)}</span></footer>
</article>
</body>
</html>";
    }

    public static string MaskLicenseKey(string? licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey)) return string.Empty;
        var parts = licenseKey.Trim().Split('-');
        if (parts.Length >= 4)
            return $"{parts[0]}-{parts[1]}-{parts[2]}-•••••";
        return "HY-LCL-•••••";
    }

    static IEnumerable<string> SplitTerms(string terms) =>
        terms.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static string Money(decimal amount, string currency) =>
        $"{amount.ToString("N0", CultureInfo.InvariantCulture)} {E(currency)}";

    static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    static string Method(string method, bool ar) => method switch
    {
        "bank_transfer" => ar ? "تحويل بنكي" : "Bank transfer",
        "cash" => ar ? "كاش" : "Cash",
        "other" => ar ? "أخرى" : "Other",
        _ => method.Replace('_', ' '),
    };

    static string Status(string status, bool ar) => status.ToLowerInvariant() switch
    {
        "paid" => ar ? "مدفوع" : "Paid",
        "partial" => ar ? "جزئي" : "Partial",
        "unpaid" => ar ? "غير مدفوع" : "Unpaid",
        _ => status,
    };

    sealed class Copy
    {
        public string Kicker = "", DocTitle = "", ContractNo = "", Issued = "", Reprint = "", Seller = "", Customer = "", Owner = "";
        public string Product = "", Finance = "", Payments = "", License = "", TermsTitle = "", Ack = "";
        public string SignSeller = "", SignCustomer = "", SignLine = "", Sku = "", Item = "", Type = "", Qty = "", Unit = "", Disc = "", Line = "";
        public string Original = "", Discount = "", Final = "", Paid = "", Outstanding = "", Status = "", Date = "", Method = "", Ref = "";
        public string Edition = "", Devices = "", LicenseRef = "", LicenseIssued = "", GymCode = "", GymCodePending = "", LicensePending = "";
        public string Masked = "", SnapshotNote = "", UnpaidNote = "", PartialNote = "", PaidNote = "", NoPayments = "";
        public string NotTax = "", NotMember = "", SellerNote = "", AckText = "", TermsIntro = "", Legal = "", Foot = "", Page1 = "", Page2 = "";
    }

    static readonly Copy En = new()
    {
        Kicker = "Commercial sales contract",
        DocTitle = "HyMotion Local — Sales Contract",
        ContractNo = "Contract no.",
        Issued = "Issue date",
        Reprint = "Reprint does not create a new contract.",
        Seller = "Seller",
        Customer = "Customer",
        Owner = "Owner",
        Product = "Purchased product",
        Finance = "Financial summary at issue",
        Payments = "Payments recorded at issue",
        License = "License",
        TermsTitle = "Licensing and commercial terms",
        Ack = "Customer acknowledgment",
        SignSeller = "For HyMotion",
        SignCustomer = "Customer / gym owner",
        SignLine = "Name / Signature / Date",
        Sku = "SKU",
        Item = "Item",
        Type = "Type",
        Qty = "Qty",
        Unit = "Unit price",
        Disc = "Discount",
        Line = "Line total",
        Original = "Original price",
        Discount = "Discount",
        Final = "Final price",
        Paid = "Paid at issue",
        Outstanding = "Outstanding at issue",
        Status = "Payment status",
        Date = "Date",
        Method = "Method",
        Ref = "Reference",
        Edition = "Edition",
        Devices = "Allowed computers",
        LicenseRef = "License reference",
        LicenseIssued = "License issued",
        GymCode = "Gym code",
        GymCodePending = "— (reported after first activation)",
        LicensePending = "License not issued at the time of this paper.",
        Masked = "Full license key is not printed. HyMotion keeps the working key in Operation Center.",
        SnapshotNote = "Paid and outstanding on this contract are frozen at issue. Later payments change the live sale record, not this paper.",
        UnpaidNote = "This contract was issued with an outstanding balance. Outstanding is not zero.",
        PartialNote = "This is not a fully paid sale. The remaining amount stays due unless HyMotion records a later payment on the live sale.",
        PaidNote = "The live sale was fully paid when this contract was issued.",
        NoPayments = "No payment recorded at issue.",
        NotTax = "This document is not a tax invoice.",
        NotMember = "This is not a gym-member membership contract.",
        SellerNote = "HyMotion Local customer sales contract.",
        AckText = "I confirm that I am buying HyMotion Local for the gym named above, that the paid and outstanding amounts on this page were correct on the issue date, and that I have read the terms on page 2.",
        TermsIntro = "These terms describe how HyMotion Local works in the product. They are operational terms, not a substitute for legal advice.",
        Legal = "Draft operational language. Have a qualified lawyer review ownership, resale, refund, warranty, and governing law before treating this as a live commercial instrument.",
        Foot = "HyMotion Local Customer Sales Contract · issued snapshot",
        Page1 = "Page 1 of 2",
        Page2 = "Page 2 of 2",
    };

    static readonly Copy Ar = new()
    {
        Kicker = "عقد بيع تجاري",
        DocTitle = "HyMotion Local — عقد بيع للعميل",
        ContractNo = "رقم العقد",
        Issued = "تاريخ الإصدار",
        Reprint = "إعادة الطباعة لا تنشئ عقدًا جديدًا.",
        Seller = "البائع",
        Customer = "العميل",
        Owner = "المالك",
        Product = "المنتج المشترى",
        Finance = "الملخص المالي وقت الإصدار",
        Payments = "الدفعات المسجّلة وقت الإصدار",
        License = "الترخيص",
        TermsTitle = "شروط الترخيص والبيع",
        Ack = "إقرار العميل",
        SignSeller = "عن HyMotion",
        SignCustomer = "العميل / مالك النادي",
        SignLine = "الاسم / التوقيع / التاريخ",
        Sku = "الصنف",
        Item = "البند",
        Type = "النوع",
        Qty = "الكمية",
        Unit = "سعر الوحدة",
        Disc = "الخصم",
        Line = "إجمالي البند",
        Original = "السعر الأصلي",
        Discount = "الخصم",
        Final = "السعر النهائي",
        Paid = "المدفوع وقت الإصدار",
        Outstanding = "المتبقي وقت الإصدار",
        Status = "حالة الدفع",
        Date = "التاريخ",
        Method = "الطريقة",
        Ref = "المرجع",
        Edition = "الإصدار",
        Devices = "أجهزة مسموحة",
        LicenseRef = "مرجع الترخيص",
        LicenseIssued = "تاريخ إصدار الترخيص",
        GymCode = "كود النادي",
        GymCodePending = "— (يظهر بعد أول تفعيل)",
        LicensePending = "لم يُصدر ترخيص في وقت هذه الورقة.",
        Masked = "مفتاح الترخيص الكامل لا يُطبع. المفتاح العامل يبقى في مركز التشغيل.",
        SnapshotNote = "المدفوع والمتبقي في هذا العقد مجمّدان وقت الإصدار. أي دفعة لاحقة تغيّر سجل البيع الحي، وليس هذه الورقة.",
        UnpaidNote = "صدر هذا العقد والرصيد المتبقي قائم. المتبقي ليس صفرًا.",
        PartialNote = "هذا البيع غير مكتمل الدفع. المبلغ المتبقي يبقى مستحقًا ما لم تسجّل HyMotion دفعة لاحقة على سجل البيع الحي.",
        PaidNote = "كان البيع الحي مدفوعًا بالكامل عند إصدار هذا العقد.",
        NoPayments = "لا توجد دفعة مسجّلة وقت الإصدار.",
        NotTax = "هذه الوثيقة ليست فاتورة ضريبية.",
        NotMember = "هذه ليست عقد عضوية لأعضاء النادي.",
        SellerNote = "عقد بيع عميل HyMotion Local.",
        AckText = "أقر أنني أشتري HyMotion Local للنادي المذكور أعلاه، وأن مبلغي المدفوع والمتبقي في هذه الصفحة كانا صحيحين في تاريخ الإصدار، وأنني قرأت الشروط في الصفحة 2.",
        TermsIntro = "هذه البنود تصف سلوك المنتج. هي شروط تشغيلية وليست استشارة قانونية.",
        Legal = "صياغة تشغيلية. يلزم مراجعة محامٍ مختص لمسائل الملكية وإعادة البيع والاسترداد والضمان والقانون الواجب قبل الاستخدام التجاري الفعلي.",
        Foot = "عقد بيع عميل HyMotion Local · لقطة إصدار",
        Page1 = "صفحة 1 من 2",
        Page2 = "صفحة 2 من 2",
    };
}
