from pathlib import Path

replacements = {
    Path(r"d:\GMS\GMS\GMS.Application\Services\InvoicePdfModelFactory.cs"): [
        ("GymFlowPro Gym", "HyMotion Gym"),
    ],
    Path(r"d:\GMS\GMS\GMS.Application\Services\InvoiceDocumentHtmlBuilder.cs"): [
        ("GymFlowPro Gym", "HyMotion Gym"),
    ],
    Path(r"d:\GMS\GMS\GMS.Application\Services\AccessCardHtmlBuilder.cs"): [
        ("GymFlowPro Gym", "HyMotion Gym"),
        (">GymFlowPro</div>", ">HyMotion</div>"),
    ],
    Path(r"d:\GMS\GMS\GMS.Infrastructure\Services\InvoicePdfRenderer.cs"): [
        ("GymFlowPro Gym", "HyMotion Gym"),
    ],
    Path(r"d:\GMS\GMS\GMS.Infrastructure\Services\FourJawalyWhatsAppService.cs"): [
        ('?? "GymFlowPro"', '?? "HyMotion"'),
    ],
    Path(r"d:\GMS\GMS\GMS.Infrastructure\Services\FawryService.cs"): [
        ("GymFlowPro Sale", "HyMotion Sale"),
    ],
    Path(r"d:\GMS\GMS\GMS.Infrastructure\Services\PaymobService.cs"): [
        ('last_name = "GymFlowPro"', 'last_name = "HyMotion"'),
    ],
}

for path, reps in replacements.items():
    text = path.read_text(encoding="utf-8")
    original = text
    for old, new in reps:
        text = text.replace(old, new)
    if text != original:
        path.write_text(text, encoding="utf-8")
        print("updated", path.name)
    else:
        print("no change", path.name)

for appsettings in [
    Path(r"d:\GMS\GMS\GMS.Api\appsettings.json"),
    Path(r"d:\GMS\GMS\GMS.Api\appsettings.Development.json"),
    Path(r"d:\GMS\GMS\GMS.Api\appsettings.Staging.json"),
    Path(r"d:\GMS\GMS\GMS.Api\appsettings.Production.json"),
]:
    if not appsettings.exists():
        continue
    text = appsettings.read_text(encoding="utf-8")
    original = text
    text = text.replace('"FromName": "GymFlowPro"', '"FromName": "HyMotion"')
    text = text.replace('"ApplicationName": "Gym Flow Pro"', '"ApplicationName": "HyMotion"')
    text = text.replace('"ApplicationName": "GymFlowPro"', '"ApplicationName": "HyMotion"')
    if text != original:
        appsettings.write_text(text, encoding="utf-8")
        print("updated", appsettings.name)
    else:
        print("no change", appsettings.name)
