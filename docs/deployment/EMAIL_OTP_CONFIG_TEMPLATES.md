# EMAIL OTP - CONFIGURATION TEMPLATES

Copy your preferred template to `GMS.Api\appsettings.json` under the `EmailSettings` section.

---

## 📧 GMAIL (RECOMMENDED FOR TESTING)

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": 587,
  "SmtpUser": "your-email@example.com",
  "SmtpPassword": "xxxx xxxx xxxx xxxx",
  "SendGridApiKey": "",
  "FromAddress": "your-email@example.com",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**How to get password:**
1. https://myaccount.google.com/security
2. Enable 2-Step Verification
3. https://myaccount.google.com/apppasswords
4. Select Mail + Windows
5. Copy 16-character password

**Common Issues:**
- 2-Step Verification not enabled → enable it first
- App passwords not available → some corporate accounts disable this
- Try personal Gmail account

---

## 📧 OFFICE 365 / OUTLOOK

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "smtp.office365.com",
  "SmtpPort": 587,
  "SmtpUser": "your-email@yourdomain.com",
  "SmtpPassword": "YOUR_SMTP_APP_PASSWORD",
  "SendGridApiKey": "",
  "FromAddress": "your-email@yourdomain.com",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**Notes:**
- Use your actual password (not app-specific)
- Works with corporate/custom domains
- STARTTLS on 587

---

## 📧 MAILGUN (FREE TIER)

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "smtp.mailgun.org",
  "SmtpPort": 587,
  "SmtpUser": "postmaster@sandbox1234.mailgun.org",
  "SmtpPassword": "YOUR_SMTP_APP_PASSWORD",
  "SendGridApiKey": "",
  "FromAddress": "noreply@sandbox1234.mailgun.org",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**Get Started:**
1. Sign up: https://mailgun.com
2. Free tier includes 100 emails/day
3. Get SMTP credentials from domain settings
4. Copy `SmtpUser` and `SmtpPassword`

---

## 📧 SENDGRID (ALTERNATIVE)

```json
"EmailSettings": {
  "Provider": "sendgrid",
  "SmtpHost": "",
  "SmtpPort": 0,
  "SmtpUser": "",
  "SmtpPassword": "",
  "SendGridApiKey": "SG.xxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "FromAddress": "noreply@yourdomain.com",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**Get Started:**
1. Sign up: https://sendgrid.com
2. Get API key from settings
3. Create sender (verified email)
4. Requires custom SendGridOtpDeliveryStrategy (future enhancement)

**Note:** SendGrid support not implemented yet in current version.

---

## 📧 AWS SES (AMAZON)

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "email-smtp.region.amazonaws.com",
  "SmtpPort": 587,
  "SmtpUser": "your-smtp-username",
  "SmtpPassword": "YOUR_SMTP_APP_PASSWORD",
  "SendGridApiKey": "",
  "FromAddress": "noreply@yourdomain.com",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**Get Started:**
1. AWS SES Dashboard → SMTP Settings
2. Create SMTP credentials
3. Verify sending email
4. Copy SMTP username/password

---

## 📧 SMTP2GO (SIMPLE & CHEAP)

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "mail.smtp2go.com",
  "SmtpPort": 587,
  "SmtpUser": "api-user",
  "SmtpPassword": "YOUR_SMTP_APP_PASSWORD",
  "SendGridApiKey": "",
  "FromAddress": "noreply@yourdomain.com",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

---

## 📧 LOCALHOST SMTP (TESTING ONLY)

For local testing without real SMTP:

```json
"EmailSettings": {
  "Provider": "smtp",
  "SmtpHost": "localhost",
  "SmtpPort": 25,
  "SmtpUser": "",
  "SmtpPassword": "",
  "SendGridApiKey": "",
  "FromAddress": "test@localhost",
  "FromName": "GymFlowPro",
  "OtpTtlMinutes": 5
}
```

**Requirements:**
- Local SMTP server running (MailHog, etc.)
- Only for development/testing
- No authentication

---

## 🔑 QUICK REFERENCE

| Provider | Host | Port | User | Password | Notes |
|----------|------|------|------|----------|-------|
| Gmail | smtp.gmail.com | 587 | your-email@example.com | 16-char app pwd | App password required |
| Office 365 | smtp.office365.com | 587 | email@domain | actual pwd | Corporate ready |
| Mailgun | smtp.mailgun.org | 587 | postmaster@... | smtp pwd | Free tier available |
| AWS SES | email-smtp.region.amazonaws.com | 587 | username | password | Reliable, cost |
| SMTP2GO | mail.smtp2go.com | 587 | api-user | api key | Simple setup |

---

## ✅ HOW TO UPDATE

1. **Open**: `GMS.Api\appsettings.json`
2. **Find**: `"EmailSettings"` section (around line 65)
3. **Replace**: Entire section with your provider's template above
4. **Save**: File
5. **Restart**: `dotnet run`

---

## 🧪 TEST AFTER UPDATE

```bash
# Send OTP
curl -X POST http://localhost:5000/api/auth/member-otp \
  -H "Content-Type: application/json" \
  -d '{"phoneNumber": "+201070498179", "gymCode": "GYM-Test-01"}'

# Expected: 200 with "Verification code sent to..."
```

---

## ⚠️ SECURITY NOTES

**Development**:
- Plain text passwords in appsettings.json OK for local dev
- Each developer uses their own credentials
- Commit `.gitignore` to prevent secrets leaking

**Production**:
- ❌ DO NOT commit real credentials
- ✅ Use secrets management:
  - Azure Key Vault
  - AWS Secrets Manager
  - HashiCorp Vault
  - Local Secrets (in production, injected at runtime)

**Example (AWS Secrets Manager in production)**:
```json
"EmailSettings": {
  "SmtpUser": "${SMTP_USER}",           // Injected from environment
  "SmtpPassword": "YOUR_SMTP_APP_PASSWORD"    // Injected from environment
}
```

---

## 🆘 COMMON ERRORS

| Error | Cause | Fix |
|-------|-------|-----|
| "Failed to send verification email" | Wrong credentials | Verify SMTP settings |
| "Connection refused" | SMTP server down | Check host/port correct |
| "Authentication failed" | Wrong password | Get new app password |
| "TLS error" | Port/protocol mismatch | Try port 465 with SSL |
| "Not allowed to send" | Gmail account locked | Check "Less secure apps" setting |

---

**Ready?** Pick a provider above and update your `appsettings.json` now! 🚀
