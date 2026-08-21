# GymFlowPro — Complete UI/UX Design Prompts
## For Claude Sonnet / Opus / Haiku

> **How to use:** Copy any prompt below and send it directly to Claude. Each prompt is self-contained and references the GymFlowPro API. Use **Opus** for complex multi-screen designs, **Sonnet** for individual screens, and **Haiku** for quick components.

---

## 🎨 DESIGN SYSTEM PROMPT (Run This First)

```
Design a complete UI design system for "GymFlowPro" — a multi-tenant gym management SaaS platform serving Egyptian gyms. The app has two interfaces: a Flutter mobile app for members (Arabic/English bilingual) and a web admin dashboard for gym staff (Owner, Manager, Trainer roles).

Create an HTML design system reference page that includes:

**Brand Identity:**
- App name: GymFlowPro
- Primary market: Egypt (supports Arabic RTL + English LTR)
- Tone: Professional, energetic, trustworthy — like a premium fitness brand meets clean fintech UI

**Design Direction:**
- Choose a bold, distinctive aesthetic — NOT generic SaaS purple gradients
- Consider: deep charcoal + electric lime accent (gym/performance energy), or warm sand + deep teal (Egyptian heritage meets modern), or midnight navy + gold (premium club aesthetic)
- Typography: pair a strong display font (for headings/numbers) with a clean body font that supports Arabic
- Color system: primary, secondary, accent, semantic colors (success/warning/danger/info), surface levels
- Spacing scale: 4px base unit
- Border radius: pill buttons, soft cards
- Shadow system: elevation levels

**Components to show:**
1. Color palette swatches with hex values and usage labels
2. Typography scale (h1–h6, body, caption, label) in both EN and AR
3. Button variants (primary, secondary, ghost, danger, icon-only)
4. Input fields (default, focused, error, disabled)
5. Card variants (stat card, member card, membership card)
6. Status badges (active, expired, frozen, cancelled, pending)
7. Navigation bar (mobile bottom nav + web sidebar)
8. Avatar component with initials fallback
9. Loading skeleton shimmer
10. Toast/snackbar notification component

Show all components in both light and dark mode side by side.
Output as a single HTML file with embedded CSS and no external dependencies except Google Fonts.
```

---

## 📱 FLUTTER MOBILE APP PROMPTS

---

### PROMPT 1 — Member Splash & Onboarding

```
Design a Flutter-style mobile UI mockup in HTML/CSS for the GymFlowPro member app onboarding flow. Show 3 screens in a horizontal scrollable phone frame layout (375×812px each):

**Screen 1 — Splash Screen**
- GymFlowPro logo centered with animated pulse ring
- Tagline in English and Arabic: "Your gym, your way / صالتك، بطريقتك"
- Full-screen background with gym atmosphere (CSS geometric pattern, no images)
- Auto-navigates after 2 seconds

**Screen 2 — Gym Code Entry**
- Header: "Welcome to your gym / مرحباً بك في صالتك"
- Large input field for gym code (e.g., GYM-CAIRO-01) with monospace font, character-by-character animation hint
- "What is my gym code?" helper link
- QR code scan button (camera icon) as alternative
- CTA button: "Continue / متابعة"
- API: POST /api/auth/member-otp (first step sends gymCode + phoneNumber)

**Screen 3 — Phone Number + OTP Verification**
- Egyptian flag + phone prefix (+20) in the input field
- Large phone number input with numeric keyboard hint
- After entry → transforms into 6-box OTP digit input (animated transition)
- Countdown timer "Resend in 0:45" with progress ring
- API: POST /api/auth/member-otp then POST /api/auth/member-verify
- Success: JWT stored in flutter_secure_storage, navigate to home

Use a dark gym aesthetic: near-black backgrounds, electric accent color, bold typography. Make the phone frames realistic with status bar and home indicator.
```

---

### PROMPT 2 — Member Home Screen

```
Design a Flutter-style mobile home screen mockup in HTML for the GymFlowPro member app.

**Layout (375×812px phone frame):**

Top section — personalized header:
- "Good morning, Ahmed / صباح الخير أحمد" with time-based greeting logic
- Notification bell icon (badge with unread count)
- Avatar circle (initials "AA" fallback)

Membership status card (hero element):
- Large card showing current plan name in EN + AR
- Visual progress bar for days remaining (e.g., "22 days remaining / 22 يوم متبقي")
- Plan type badge (Monthly Unlimited / شهري غير محدود)
- Expiry date
- Status badge: active (green), frozen (blue), expired (red)
- Data from: GET /api/memberships/{memberId}/current → MembershipDto

QR Code check-in button:
- Large, prominent button with QR icon
- "Tap to check in / اضغط للدخول"
- Opens camera to scan gym's static QR code
- API: POST /api/attendance/qr-checkin with gymCode
- Shows success overlay with member name + plan on scan

Recent activity strip:
- Horizontal scroll of last 5 check-in dates with entry method icon (QR/manual)
- "View all" link
- Data from: MemberDetailDto.recentAttendance

Quick stats row (3 cards):
- Check-ins this month
- Sessions remaining (null shows ∞)
- Invitation quota remaining

Bottom navigation:
- 4 tabs: Home (ti-home), My Membership (ti-id-badge), Invite (ti-user-plus), Notifications (ti-bell)

Arabic RTL toggle button in top corner for language switch. Make the design energetic, premium, dark-themed with gym energy.
```

---

### PROMPT 3 — QR Check-in Flow

```
Design a full-screen QR check-in flow for the GymFlowPro Flutter app. Show 3 states as separate phone mockups (375×812px):

**State 1 — Camera Scanner**
- Full-screen camera viewfinder (simulated with dark CSS)
- Animated corner brackets that pulse (CSS animation)
- Centered scanning reticle with sweeping scan line animation
- Top: "Scan gym QR code / امسح رمز الصالة" header with back button
- Bottom card sliding up: gym code input as fallback text entry
- API triggers: POST /api/attendance/qr-checkin { gymCode: "GYM-CAIRO-01" }

**State 2 — Success State**
- Full screen success overlay (not a popup — full immersive)
- Large animated checkmark (CSS draw animation)
- Member name in large bold text (EN + AR)
- Plan name below
- Sessions remaining (show "∞" for unlimited, show number for session_pack)
- Check-in time in local Egypt timezone
- "Welcome back!" / "أهلاً بعودتك!" message
- Auto-dismisses after 3 seconds with countdown ring

**State 3 — Error States (show all 4 as a 2×2 grid within one screen)**
- No active membership (red): "No active membership / لا يوجد اشتراك نشط"
- Membership frozen (blue-ish): "Membership frozen until [date] / مجمد حتى [تاريخ]"
- Sessions depleted (amber): "No sessions remaining / لا يوجد جلسات متبقية"
- Time restriction (orange): "Access not allowed at this time / الوصول غير مسموح الآن"

Each error shows the reason icon, bilingual message, and a "Contact staff" button.
Make the success state feel like a celebration. Dark theme throughout.
```

---

### PROMPT 4 — My Membership Screen

```
Design the "My Membership" tab screen for GymFlowPro Flutter app (375×812px).

**Top section — Active Membership Card (large hero card):**
- Plan name EN + AR in large display font
- Plan type icon (calendar for monthly, lightning for sessions, clock for time-limited, users for family)
- Status badge with color (active=green, frozen=blue, expired=red, pending=amber)
- Date range: "May 1 – Jun 1, 2026"
- Days remaining with radial progress gauge
- Amount paid + payment method chip
- Sessions remaining (if session_pack — show "18 / 20" with progress bar)
- Freeze window dates (if frozen)
- Data from: GET /api/memberships/{memberId}/current → MembershipDto

**Action buttons row (if active membership):**
- "Freeze" button (ice icon) — only if not already frozen
- "Unfreeze" button — only if frozen
- These call: POST /api/members/{id}/freeze or /unfreeze

**Membership history section:**
- Section header "History / السجل"
- List of past memberships (scrollable)
- Each item: plan name, date range, status badge, amount paid
- Data from: GET /api/memberships/{memberId}/history

**Empty state (no membership):**
- Illustration (CSS/SVG gym icon)
- "No active membership / لا يوجد اشتراك نشط"
- "Contact your gym to subscribe"

Include frozen state visual: a subtle ice/blue tint on the hero card with snowflake icon.
```

---

### PROMPT 5 — Guest Invitation Screen

```
Design the "Invite a Guest" screen for GymFlowPro Flutter app (family plan members). Show all states.

**Screen — Invite Flow:**
- Header with remaining quota display: "3 invitations left this month / 3 دعوات متبقية هذا الشهر"
- Visual quota indicator: 3 filled circles + empty circles (up to plan max)

Form fields:
- Guest name input (required)
- Guest phone number input (Egyptian phone format +20)
- Visit date picker (date selector showing calendar)
- CTA: "Send Invitation / إرسال الدعوة"
- API: POST /api/invitation/send

**Success state (inline, no full-screen):**
- Animated confetti burst (CSS)
- "Invitation sent! / تم إرسال الدعوة!"
- Shows: guest name, visit date, quota now "2 left"
- SendInvitationResponse data shown

**Invitation History list:**
- Each card: guest name, visit date, status badge
- Status badges: sent (gray), visited (blue), converted (green), expired (red/muted)
- Data from: GET /api/invitation/history → InvitationHistoryResponse
- Converted guests show a small "became a member!" badge

**Quota exceeded state:**
- Locked overlay on form
- "Monthly quota reached / تم استنفاد الحصة الشهرية"
- Resets counter showing days until next month

Make this feel warm and social — inviting friends to workout together. Use lighter, friendlier colors vs the dark check-in screens.
```

---

### PROMPT 6 — Notifications Screen

```
Design the Notifications screen for GymFlowPro Flutter app.

**Screen layout (375×812px):**

Header:
- "Notifications / الإشعارات"
- "Mark all read" text button (top right)
- Unread count badge on the tab bar icon

Notification list:
Each notification card shows:
- Icon representing type (bell for expiry, checkmark for check-in, info for general)
- Title EN + AR (primary/secondary text)
- Body preview text (2 lines, truncated)
- Time ago (relative: "2 hours ago / منذ ساعتين")
- Unread indicator: left accent bar or blue dot
- Swipe-to-dismiss gesture visual hint
- API: GET /api/notifications → NotificationDto
- Tap: POST /api/notifications/{id}/read

**Notification types to show (4 examples in the list):**
1. 🔴 UNREAD — "Membership Expiring / اشتراكك على وشك الانتهاء" — "Your membership expires in 3 days"
2. 🔵 UNREAD — "Check-in Confirmed / تم تسجيل دخولك" — "Welcome! You checked in at 8:30 AM"
3. ⚪ READ — "Gym Closed Tomorrow / الصالة مغلقة غداً" — "Maintenance day"
4. ⚪ READ — "New Plan Available / خطة جديدة متاحة" — "Check out our new family plan"

Channel chip on each notification (push/whatsapp icon)

**Empty state:**
- Centered bell icon with "no new notifications / لا توجد إشعارات جديدة"

Use the bilingual layout naturally — show the Arabic title as secondary text under the English title.
```

---

## 🖥️ WEB ADMIN DASHBOARD PROMPTS

---

### PROMPT 7 — Staff Login Page

```
Design the GymFlowPro staff login page as a full HTML webpage.

**Layout:** Split screen — left 40% branding panel, right 60% login form.

Left panel (brand):
- GymFlowPro logo + wordmark
- Gym imagery via CSS geometric pattern (no images)
- Tagline: "Manage your gym with clarity"
- 3 feature bullets with icons

Right panel (login form):
- "Welcome back" heading
- "Staff Portal" sub-label
- Email input field
- Password input with show/hide toggle
- Gym Code input (required — identifies the tenant for multi-tenancy)
  - Helper tooltip explaining what gym code is
- "Remember me" checkbox
- Login button (primary, full width)
- Forgot password link
- API: POST /api/auth/login → { email, password, gymCode }

**States to show:**
- Default form
- Loading state (button spinner)
- Error state: "Invalid credentials or gym code" inline alert

**Token behavior note (shown as info banner):**
- Access token: 15 min, Refresh token: 30 days, auto-refresh on 401

Role context chips below the form:
- Shows: Owner | Manager | Trainer role descriptions
- "Your role is determined by your administrator"

Make the design premium and confident — this is a B2B product that gym owners trust with their business. Dark sidebar, clean white form area.
```

---

### PROMPT 8 — Main Dashboard (Analytics Overview)

```
Design the GymFlowPro web admin dashboard main page as a fully working HTML/CSS/JS page.

**Layout:** Fixed left sidebar (220px) + main content area.

**Left Sidebar Navigation:**
- GymFlowPro logo
- Nav items with icons:
  - Dashboard (ti-layout-dashboard) — active
  - Members (ti-users)
  - Memberships (ti-id-badge)
  - Plans (ti-package)
  - Attendance (ti-door-enter)
  - Reports (ti-chart-bar)
  - Notifications (ti-bell)
  - Staff (ti-user-shield) — Owner only
  - Settings (ti-settings)
- Bottom: user avatar + name + role badge + logout

**Top Bar:**
- Breadcrumb: Dashboard
- Date range picker (today/week/month)
- "Live" indicator with pulsing green dot
- Gym name from TenantSettingsDto

**KPI Cards row (4 cards):**
- Active Members: 150 ↑12 this month
- Revenue This Month: EGP 75,000
- Check-ins Today: 45
- Check-ins This Week: 280
- Data: GET /api/analytics/overview → DashboardOverviewDto

**Charts section (2 columns):**
Left (60%): Revenue Line Chart (6 months)
- Labels: ["Dec", "Jan", "Feb", "Mar", "Apr", "May"]
- Values: [65000, 70000, 72000, 68000, 71000, 75000]
- Data: GET /api/analytics/revenue?months=6
- Render as a real Chart.js line chart

Right (40%): Member Status Pie/Donut Chart
- Active: 150, Expired: 30, Frozen: 5, Cancelled: 2
- Data: GET /api/analytics/members-status
- Render as Chart.js doughnut chart with legend

**Bottom section (2 columns):**
Left: Today's Live Attendance Feed
- List of recent check-ins with member name, time, entry method icon
- Auto-refreshes (SignalR /hubs/attendance)
- "QR" or "Manual" badge on each entry
- Data: GET /api/attendance/today

Right: Invitation Funnel (horizontal funnel bars)
- Sent: 200 → Visited: 80 → Converted: 25 (12.5% rate)
- Data: GET /api/analytics/invitations → InvitationFunnelDto

Use Chart.js from cdnjs. Make the dashboard feel like premium fintech/analytics. Dark sidebar, clean light content area. Show real data in charts.
```

---

### PROMPT 9 — Members List & Search

```
Design the Members management page for GymFlowPro web admin dashboard.

**Layout:** Standard sidebar + content area (reference Prompt 8 sidebar).

**Page header:**
- "Members / الأعضاء" title with total count badge
- Search bar (searches by name, phone, member number)
- Filter chips: All | Active | Expired | Frozen | Cancelled
- Sort dropdown: Name | Expiry Date | Join Date
- "Add Member" button (primary)
- API: GET /api/members?search=&status=&page=1&pageSize=20

**Members table:**
Columns:
- Checkbox (bulk select)
- Avatar + Member # + Name (EN / AR sub-text)
- Phone
- Current Plan (EN name)
- Status badge (color coded: active=green, expired=red, frozen=blue, cancelled=gray)
- Expiry Date (highlight red if < 7 days)
- Join Date
- Actions: View (ti-eye), Edit (ti-edit), More (ti-dots-vertical)

**Table features:**
- Row hover highlight
- Pagination controls (Previous / Page 1 of 8 / Next)
- 20 items per page default
- Loading skeleton rows (3 animated shimmer rows)

**Bulk actions bar (appears when rows selected):**
- "X members selected"
- Send Notification button
- Export button

**Empty search state:**
- Illustration + "No members found matching your search"

**Quick stats above table:**
- 4 small chips: 150 Active • 30 Expired • 5 Frozen • 2 Cancelled

Make the table clean and data-dense but readable. Use Egyptian gym member names in the sample data. Arabic name shown as secondary text under English name.
```

---

### PROMPT 10 — Member Detail Page

```
Design the individual member detail page for GymFlowPro web admin.

**API:** GET /api/members/{id} → MemberDetailDto

**Layout:** Sidebar + 2-column content (left 35% profile, right 65% details)

**Left column — Member Profile Card:**
- Large avatar circle with initials (or profile photo)
- Member number badge (MEM-001)
- Full name EN + AR
- Phone number (clickable tel:)
- Email
- Date of birth + age calculation
- Join date
- isActive status toggle (with confirmation modal)
- Quick actions: Edit (ti-edit), Deactivate (ti-trash) — OwnerOnly
- Invitation quota remaining (ti-user-plus icon + number)

**Right column — Tabs:**

Tab 1: Membership
- Current membership hero card:
  - Plan name + type badge
  - Status (color coded)
  - Progress bar: days remaining / total days
  - Date range
  - Amount paid + payment method
  - Sessions remaining (if applicable)
  - Freeze/Unfreeze buttons → POST /api/members/{id}/freeze or /unfreeze
  - Freeze modal: frozenUntil date picker + reason text field
- Renew button → POST /api/memberships/{memberId}/renew
- Assign new plan button (if no active) → POST /api/memberships/{memberId}/assign

Tab 2: Attendance History
- Table: Date | Check-in Time | Entry Method | Duration
- Paginated list from GET /api/members/{id}/attendance
- Entry method icon: QR (ti-qrcode) or Manual (ti-user-check)
- Calendar heatmap showing attendance pattern (12-week view)

Tab 3: Membership History
- Timeline list of all past memberships
- Each: Plan name, status badge, date range, amount paid
- Data: GET /api/memberships/{memberId}/history

**Action buttons (sticky bottom bar on mobile, inline on desktop):**
- Edit Member → PUT /api/members/{id}
- Send Notification → POST /api/notifications/send-bulk

Show a realistic Egyptian member: Ahmed Ali / أحمد علي, Monthly Unlimited plan, 22 days remaining.
```

---

### PROMPT 11 — Add/Edit Member Modal

```
Design the Add Member modal and Edit Member drawer for GymFlowPro web admin.

**Add Member — Full Modal (centered overlay, 560px wide):**
Header: "Add New Member / إضافة عضو جديد"

Form sections:

Section 1 — Personal Info:
- Full Name (English)* — text input
- Full Name (Arabic)* — text input with RTL direction
- Phone Number* — with +20 Egyptian prefix, validates format
- Date of Birth* — date picker
- Email — optional

Section 2 — Additional Info (collapsible):
- National ID — optional
- Emergency Contact — optional phone field
- Notes — textarea

Footer:
- Cancel button (ghost)
- "Create Member" button (primary) — disabled until required fields valid
- API: POST /api/members → CreateMemberRequest

**Validation states:**
- Real-time phone format validation
- Duplicate phone error (400 response): "Phone number already registered / رقم الهاتف مسجل مسبقاً"
- Required field errors shown inline

**Edit Member — Right Slide Drawer (400px):**
- Same fields but pre-populated
- Shows last updated timestamp
- API: PUT /api/members/{id} → UpdateMemberRequest
- "Save Changes" + "Cancel" in footer

**Assign Membership Modal (triggered from member detail):**
- Plan selector dropdown (from GET /api/membership-plans)
  - Shows plan name, price, duration, type badge
- Payment method radio: Cash | Paymob | Fawry
- Cash → immediate activation
- Gateway → "pending" status, activated via webhook
- API: POST /api/memberships/{memberId}/assign → AssignMembershipRequest

Design all three components with consistent modal/drawer patterns. Show error and loading states.
```

---

### PROMPT 12 — Membership Plans Management

```
Design the Membership Plans page for GymFlowPro web admin (Owner role only).

**Page layout:** Cards grid view (not table) — plans feel like product cards.

**Plans grid (3 columns, responsive):**
Each plan card shows:
- Plan name EN (large) + AR (smaller, muted)
- Plan type badge with icon:
  - monthly_unlimited → calendar icon, blue
  - session_pack → lightning icon, amber
  - time_limited → clock icon, purple
  - pt_credits → dumbbell icon, teal
  - family → users icon, coral
- Price: "EGP 500" in large display font
- Duration: "30 days"
- Special fields (conditional):
  - session_pack: "20 sessions"
  - time_limited: "8:00 AM – 5:00 PM"
  - family: "5 guest invitations/month"
- Stats: "45 active memberships" / "120 total"
- isActive toggle switch
- Edit button (ti-edit) + Delete button (ti-trash) with 409 conflict guard
- API: GET /api/membership-plans → List<PlanListItemDto>

**"Create Plan" button** → opens modal below.

**Create/Edit Plan Modal (700px wide):**
- Plan name EN* + AR*
- Plan type selector (5 radio cards with icon + description)
- Price (EGP) + Duration (days)
- Conditional fields (shown/hidden based on plan type):
  - session_pack: SessionCount radio (10 | 20 | 50)
  - time_limited: Time range picker (Start/End in HH:mm format)
  - family: Invitation quota number input
- Description EN + AR (optional textarea)
- Preview card (live preview of how the plan card looks)
- API: POST /api/membership-plans → CreatePlanRequest

**Delete confirmation:**
- If plan has active memberships → show error: "409 — Cannot delete plan with 45 active memberships"
- If safe → confirmation dialog

Make plan cards feel like a pricing page — premium, well-structured.
```

---

### PROMPT 13 — Attendance / Check-in Management

```
Design the Attendance management page for GymFlowPro web admin.

**Layout:** Three-panel design.

**Panel 1 — Live Dashboard (top bar across full width):**
- Real-time counter: "45 members checked in today"
- Pulsing green dot "Live" label
- Peak time indicator: "Peak: 10:00-11:00"
- Connected via SignalR /hubs/attendance

**Panel 2 — Today's Attendance (left 60%):**
Table: Member # | Name | Check-in Time | Entry Method | Plan
- Filter tabs: All | QR | Manual
- QR entries: green QR icon badge
- Manual entries: orange hand icon badge
- Real-time updates (new rows appear at top with highlight animation)
- API: GET /api/attendance/today?filter=all → List<TodayAttendanceDto>

**Panel 3 — Manual Check-in (right 40%):**
- Title: "Manual Check-in / تسجيل يدوي"
- Search box: "Search member by name, phone, or ID"
- Real-time search results (as user types):
  - Each result: avatar, name, membership status badge, plan name
  - isSelectable: true → clickable, green
  - isSelectable: false → grayed out, shows unselectableReason
- API: GET /api/attendance/search-members?query=
- On member select:
  - Reason selector (4 radio options):
    1. Dead phone / الهاتف فارغ
    2. No app yet / لم يثبت التطبيق
    3. App issue / مشكلة في التطبيق
    4. Other / أخرى (shows notes field)
  - "Check In" button
  - API: POST /api/attendance/manual-checkin → ManualCheckinRequest
- Success: shows member name + plan + sessions remaining

**Attendance Heatmap section (below):**
- 7×24 grid (Mon–Sun × 24 hours)
- Color intensity from white → deep accent color
- Hover tooltip: "Tuesday 10:00 — 20 check-ins"
- Data: GET /api/analytics/heatmap → int[7][24]
- Row labels: Mon/Tue/Wed/Thu/Fri/Sat/Sun
- Column labels: 0h, 6h, 12h, 18h, 23h

Make the live feed feel like an airport departures board — real-time, informational, urgent.
```

---

### PROMPT 14 — Reports & Analytics

```
Design the Reports section for GymFlowPro web admin dashboard.

**Layout:** Full-width content with sidebar.

**Page header:**
- "Reports / التقارير"
- Date range picker (from / to date inputs)
- Export button (CSV)

**Section 1 — Attendance Summary:**
- Date range: May 1–31, 2026
- Bar chart (Chart.js): daily check-in count per day for the month
- Below chart: summary table with Date | Check-ins | Unique Members
- API: GET /api/reports/attendance-summary?from=2026-05-01&to=2026-05-31

**Section 2 — Revenue Detail (OwnerOnly badge):**
- Filter by payment method: All | Cash | Paymob | Fawry
- Table: Transaction Date | Member Name | Plan Name | Amount (EGP) | Payment Method
- Amount column formatted with EGP currency
- Subtotal row at bottom
- API: GET /api/reports/revenue-detail?from=&to=&method=

**Section 3 — Peak Hours (horizontal bar chart):**
- Top 5 time slots
- Each: time slot label (10:00-11:00) + horizontal progress bar + count + percentage
- API: GET /api/reports/peak-hours → List<PeakHourItemDto>

**Section 4 — Member Retention (OwnerOnly badge):**
- Large KPI cards:
  - 200 expired memberships
  - 85 renewed
  - 42.5% retention rate
- Semicircle gauge for retention rate
- Contextual benchmark: "Industry average: ~35%" (color coded)
- API: GET /api/reports/member-retention → MemberRetentionDto

**Role-based blur:**
- OwnerOnly sections show blurred overlay with lock icon for Manager/Trainer roles
- "Upgrade access: contact your owner"

Use Chart.js for all charts. Load from cdnjs.cloudflare.com. Make charts interactive with hover tooltips.
```

---

### PROMPT 15 — Staff Management (Owner Only)

```
Design the Staff Management page for GymFlowPro web admin (Owner role exclusively).

**Page layout:** Table-based staff list.

**Role badge system:**
- Owner: gold badge (ti-crown icon)
- Manager: blue badge (ti-briefcase icon)
- Trainer: teal badge (ti-barbell icon)

**Staff table:**
Columns: Avatar + Name | Email | Role | Status | Last Login | Created | Actions

Actions per row:
- Edit (ti-edit) → opens edit drawer
- Reset Password (ti-key) → opens modal
- Deactivate (ti-user-off) → confirmation

API: GET /api/admin/staff → List<StaffListItemDto>

**"Add Staff Member" button → Modal:**
- Full Name*
- Email*
- Password* (with strength indicator: weak/fair/strong/very strong)
- Role selector: Manager | Trainer (NOT Owner — owner cannot create other owners)
- API: POST /api/admin/staff → CreateStaffRequest
- Error 400: duplicate email shown inline

**Edit Staff Drawer:**
- Full Name
- Role (manager/trainer only)
- isActive toggle
- API: PUT /api/admin/staff/{id} → UpdateStaffRequest

**Reset Password Modal:**
- New password input
- Confirm password
- Password requirements checklist (8+ chars, uppercase, number, special)
- API: POST /api/admin/staff/{id}/reset-password → ResetPasswordRequest

**Last Login indicator:**
- "Today at 2:00 PM" → green
- "3 days ago" → amber
- "14+ days ago" → red (possibly inactive)
- "Never logged in" → gray with nudge button to send invite

Show 3 staff members: 1 Manager and 2 Trainers with different status examples.
```

---

### PROMPT 16 — Gym Settings Page

```
Design the Tenant/Gym Settings page for GymFlowPro web admin (Owner only).

**Layout:** Tabbed settings page.

**Tab 1 — Gym Information:**
Form with:
- Gym Name (English)* — text input
- Gym Name (Arabic)* — RTL text input
- Phone Number — with format hint
- Address — textarea
- Logo Upload — drag-and-drop zone with preview
  - Shows current logo or placeholder
  - Upload → updates logoUrl in TenantSettingsDto
- Gym Code — READ ONLY field (generated by system, shown in a code chip)
  - "Copy" button next to it
- API: GET /api/settings → TenantSettingsDto
- Save: PUT /api/settings → UpdateTenantSettingsRequest

**Tab 2 — QR Code & Check-in:**
- QR Poster preview (large card showing the gym's QR code image)
- QR poster URL: GET /api/settings/qr-poster
- "Download QR Poster" button
- "Print" button
- Instructions for staff: "Print this poster and display at gym entrance"
- QR code displays the gymCode so members can scan to check in
- Gym code display: GET /api/settings/gym-code

**Tab 3 — Subscription & Billing:**
- Placeholder section (grayed out)
- "Coming soon" badge
- Current plan: "Pro Plan — 1 active gym location"
- Tenant ID shown for support reference

**Status indicators:**
- Tenant isActive: green "Active" badge
- Created date
- Last updated date

Make this feel like a professional SaaS settings page. Clean, organized, trustworthy.
```

---

### PROMPT 17 — Notifications Management (Staff View)

```
Design the Notifications management page for GymFlowPro web admin (Manager/Owner).

**Layout:** Split view — compose (left 40%) + sent history (right 60%).

**Left — Compose Notification:**
- Title: "Send Notification / إرسال إشعار"
- Target selector:
  - Radio: "All active members" | "Specific members"
  - If specific: multi-select member search
    - Type to search, select chips appear below
    - Up to 50 members
- Message fields:
  - Title EN (required)
  - Title AR (required)
  - Body EN (required, textarea)
  - Body AR (required, textarea)
  - Character count for each
- Channel selector: Push Notification | WhatsApp
- Preview card (shows how notification looks on phone)
- "Send Now" button
- API: POST /api/notifications/send-bulk → SendBulkNotificationRequest
- Response: "Notification sent to 150 members" toast

**Right — Sent History:**
Table of sent notifications:
- Date Sent | Title (EN) | Audience | Channel | Recipients
- Channel icon: bell for push, WhatsApp icon for whatsapp
- Audience: "All members (150)" or "Ahmed Ali + 3 others"
- Expandable row: shows full EN + AR message content

**Member-facing view (toggle to "Member View"):**
- Simulated phone notification list
- Shows how messages appear to members
- Includes isRead status

Make the compose area feel like a messaging dashboard — clean, focused, with character limits prominently displayed.
```

---

## 🔧 COMPONENT-LEVEL PROMPTS

---

### PROMPT 18 — Membership Status Card Component

```
Design a reusable MembershipStatusCard React component for GymFlowPro that handles all 5 membership statuses.

Props interface:
- membership: MembershipDto | null
- compact: boolean (for list view vs detail view)

Show all 5 status variants side by side:

1. ACTIVE (green theme):
   - Progress bar showing days remaining (22/30)
   - Plan name + type badge
   - Expiry date
   - Sessions remaining (if session_pack, show "18/20")

2. FROZEN (ice blue theme):
   - Snowflake icon
   - "Frozen until May 20" 
   - Original expiry extended by freeze duration
   - "Unfreeze" action button

3. EXPIRED (red/muted theme):
   - Days since expiry: "Expired 5 days ago"
   - "Renew Now" CTA button (prominent)
   - Last plan name shown

4. PENDING (amber theme):
   - Spinner animation
   - "Awaiting payment confirmation"
   - Payment method shown (Paymob / Fawry)

5. CANCELLED (gray theme):
   - Strikethrough plan name
   - Cancellation date
   - "Contact gym" note

Compact variant: single line per status (for member list table rows).
Full variant: card with all details (for member detail page).

Data source: MembershipDto from GET /api/memberships/{memberId}/current
Implement as a real React JSX component with useState for interactive demo.
```

---

### PROMPT 19 — Real-time Attendance Feed Widget

```
Design a real-time attendance feed widget for GymFlowPro that simulates SignalR updates.

Build as interactive React component:

**Visual design:**
- Card with "Live Attendance" header + pulsing green dot
- Today's count: "47 check-ins today" (updates in real-time)
- Feed list (max 8 visible, scrollable):
  Each entry:
  - Avatar (initials circle, color based on member name hash)
  - Member name + member number
  - Time: "just now" / "2 min ago"
  - Entry method badge: QR (green) or Manual (orange)
  - Plan name chip

**Simulation:**
- Auto-generates a new random check-in every 4-8 seconds
- New entries slide in from top with smooth animation
- Counter increments
- Old entries fade and push down

**Data shape (from TodayAttendanceDto):**
{
  id, memberId, memberNumber, memberName, memberNameAr,
  checkInAtUtc, checkOutAtUtc, entryMethod, planName
}

**Interaction:**
- Click entry → sendPrompt("Show details for member MEM-001")
- "View all" button → sendPrompt("Show full attendance list for today")
- Filter toggle: All / QR / Manual

**Empty state:** Friendly "No check-ins yet today" with clock icon.

Implement with actual setInterval simulation, CSS slide-in animations, and realistic Egyptian gym member names.
```

---

### PROMPT 20 — Bilingual Input Component

```
Design a bilingual form input system for GymFlowPro that handles both English and Arabic text fields elegantly.

Build as an interactive HTML/CSS demo showing:

**BilingualInput component:**
- Side-by-side inputs in one row
- Left: English input (LTR, placeholder "Full Name")
- Right: Arabic input (RTL, dir="rtl", placeholder "الاسم الكامل")
- Visual separator between them
- Language flag/label: 🇬🇧 EN | AR 🇸🇦
- Both required (asterisk)

**Variants to show:**
1. Text input (name fields)
2. Textarea (description fields — for plan descriptions)
3. Single field with language toggle (for mobile — one field switches between EN/AR)

**Smart features:**
- Arabic detector: if user types Arabic chars in EN field → auto-switch prompt
- Character count per field
- Validation: both must be filled, or neither
- RTL auto-detection based on first character typed

**Form preview:**
Full "Create Membership Plan" form using BilingualInput components:
- Plan Name EN | Plan Name AR
- Description EN | Description AR (textarea)
- Plan Type selector (single, no bilingual needed)
- Price + Duration inputs (numeric, single)

Show filled state with realistic data:
- EN: "Monthly Unlimited Premium"
- AR: "شهري غير محدود بريميوم"

Make the bilingual pair feel intuitive, not clunky. The two inputs should feel like one unified field.
```

---

## 📊 QUICK COMPONENT PROMPTS (For Haiku)

---

### PROMPT 21 — Quick: Member Avatar Component
```
Design a member avatar component for GymFlowPro. 
Sizes: xs(24px), sm(32px), md(40px), lg(56px), xl(80px).
States: initials (2-letter, color derived from name hash), photo (with fallback), loading skeleton.
Include: role indicator dot (staff=blue, member=green, inactive=gray).
Show all sizes + states in a grid. React component.
```

### PROMPT 22 — Quick: Payment Method Badge
```
Design payment method badges for GymFlowPro:
- Cash (green, ti-cash icon)
- Paymob (blue, payment card icon)  
- Fawry (yellow, lightning icon)
- Vodafone Cash (red, phone icon)
Show in 3 sizes (sm/md/lg) with and without icon. HTML only.
```

### PROMPT 23 — Quick: Plan Type Selector
```
Design a plan type radio selector for GymFlowPro's create plan form.
5 options as large clickable radio cards:
- Monthly Unlimited (calendar icon, blue)
- Session Pack (lightning icon, amber) — shows "10/20/50 sessions" sub-selector when chosen
- Time Limited (clock icon, purple) — shows time range picker when chosen
- PT Credits (dumbbell icon, teal)
- Family Plan (users icon, coral) — shows invitation quota input when chosen
Animated expand/collapse for conditional fields. React with useState.
```

### PROMPT 24 — Quick: Attendance Heatmap
```
Build an interactive attendance heatmap for GymFlowPro.
7 rows (Mon-Sun) × 24 columns (hours 0-23).
Sample data: busiest at Tue/Thu/Sat 7-9 AM and 6-8 PM.
Color scale: empty=transparent, low=light accent, high=dark accent.
Hover tooltip: "Tuesday 10:00 — 20 check-ins".
Data from: GET /api/analytics/heatmap → int[7][24].
Compact, fits in 680px width. Interactive HTML with vanilla JS.
```

### PROMPT 25 — Quick: Session Counter
```
Design a session counter display for GymFlowPro session_pack memberships.
Shows: "18 / 20 sessions remaining" with:
- Circular progress gauge (large, centered)
- Color transitions: green (>50%) → amber (20-50%) → red (<20%)
- Individual session dots grid (20 dots, 18 filled, 2 empty)
- "2 sessions used" sub-label
- When 0 remaining: locked state with "Contact gym to renew"
React component with animated fill.
```

---

## 🚀 FULL APP PROMPT (For Opus — Use for Complete Builds)

---

### PROMPT 26 — Complete Staff Web App (Single HTML)

```
Build a complete, functional GymFlowPro staff web application as a single HTML file with embedded CSS and JavaScript. This is a full SPA simulation.

**Tech:** Vanilla HTML/CSS/JS + Chart.js from cdnjs.

**Pages to implement (client-side routing with hash):**
1. #/login — Staff login form
2. #/dashboard — Analytics overview with 4 KPI cards + revenue chart + today's attendance
3. #/members — Searchable/filterable member table with pagination
4. #/members/:id — Member detail with membership card + attendance history tabs
5. #/plans — Plan cards grid
6. #/attendance — Live attendance feed + manual check-in panel
7. #/reports — Attendance summary + revenue detail sections

**Mock data (hardcode realistic Egyptian gym data):**
- 5 sample members: Ahmed Ali / أحمد علي, Sara Mohamed / سارة محمد, etc.
- 3 membership plans: Monthly Unlimited, 20-Session Pack, Time Limited
- 10 attendance records today
- Revenue data for 6 months

**UI Requirements:**
- Fixed left sidebar navigation (220px)
- Top bar with gym name + user info + logout
- Responsive behavior (sidebar collapses on narrow)
- Dark sidebar + light content area
- Role simulation: toggle between Owner/Manager/Trainer (affects visible nav items)
- Bilingual toggle (EN/AR) on top bar

**Charts (Chart.js):**
- Revenue line chart on dashboard
- Member status doughnut chart on dashboard
- Attendance bar chart on reports page

**Interactions:**
- Member search filters the table in real-time
- Status filter chips work
- Plan type filter on plans page works
- Manual check-in search works (filters mock data)
- All forms show validation states
- Toast notifications for actions (saved, error, etc.)

**Design:**
- Choose ONE bold, distinctive aesthetic and apply it consistently
- Premium gym management app feel
- NOT generic SaaS purple/white

Make this a genuinely impressive, functional demo application. Every click should do something.
```

---

## 💡 USAGE GUIDE

| Model | Best For | Prompts to Use |
|-------|----------|----------------|
| **Claude Opus** | Full app builds, complete flows | #26, #8+#9+#10 together |
| **Claude Sonnet** | Individual screens, complex components | #1-#17 (one at a time) |
| **Claude Haiku** | Quick components, simple UI | #21-#25 |

### Tips:
1. Always start with the **Design System Prompt** (#0) to establish the visual language
2. Reference "use the same design system as defined earlier" in subsequent prompts
3. For Flutter mockups, specify: "render as realistic 375×812px phone frame mockup"
4. For RTL support, add: "ensure Arabic text renders RTL with dir='rtl'"
5. Chain prompts: build Dashboard (#8) → then Members (#9) → then Member Detail (#10)

### API Base URL for Testing:
- Local: `http://localhost:5000/api`
- Auth header: `Authorization: Bearer {token}`
- All requests need tenant context via JWT `tenant_id` claim
