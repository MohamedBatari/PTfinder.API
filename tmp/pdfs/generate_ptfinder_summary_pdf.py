from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib import colors
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, ListFlowable, ListItem
from reportlab.lib.units import mm

out_path = r"output/pdf/PTfinder.API-app-summary.pdf"

doc = SimpleDocTemplate(
    out_path,
    pagesize=A4,
    leftMargin=15*mm,
    rightMargin=15*mm,
    topMargin=12*mm,
    bottomMargin=12*mm,
)

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(
    name="TitleCompact",
    parent=styles["Heading1"],
    fontName="Helvetica-Bold",
    fontSize=17,
    leading=20,
    spaceAfter=6,
    textColor=colors.HexColor("#0F172A"),
))
styles.add(ParagraphStyle(
    name="H2Compact",
    parent=styles["Heading2"],
    fontName="Helvetica-Bold",
    fontSize=10.5,
    leading=12,
    spaceBefore=5,
    spaceAfter=2,
    textColor=colors.HexColor("#1E3A8A"),
))
styles.add(ParagraphStyle(
    name="BodyCompact",
    parent=styles["BodyText"],
    fontName="Helvetica",
    fontSize=8.9,
    leading=11,
    spaceAfter=2,
))
styles.add(ParagraphStyle(
    name="BulletCompact",
    parent=styles["BodyText"],
    fontName="Helvetica",
    fontSize=8.8,
    leading=10.8,
))
styles.add(ParagraphStyle(
    name="SmallNote",
    parent=styles["BodyText"],
    fontName="Helvetica-Oblique",
    fontSize=8.1,
    leading=10,
    textColor=colors.HexColor("#374151"),
))

story = []
story.append(Paragraph("PTfinder.API - One-Page App Summary", styles["TitleCompact"]))
story.append(Paragraph("Evidence source: code in this repository only (controllers, services, Program.cs, EF models).", styles["SmallNote"]))

story.append(Paragraph("What it is", styles["H2Compact"]))
story.append(Paragraph(
    "PTfinder.API is an ASP.NET Core 8 backend for a PTfinder marketplace that manages coach profiles, availability, bookings, billing, gifts, and communications. "
    "It exposes REST endpoints plus a SignalR notifications hub, with SQL persistence through Entity Framework Core.",
    styles["BodyCompact"],
))

story.append(Paragraph("Who it is for", styles["H2Compact"]))
story.append(Paragraph(
    "Primary persona: personal trainers/coaches using PTfinder to publish profiles, receive bookings, and manage subscriptions and payouts. "
    "Secondary users are clients booking sessions.",
    styles["BodyCompact"],
))

story.append(Paragraph("What it does", styles["H2Compact"]))
feature_bullets = [
    "Manages coach directory data and searchable discovery by category, speciality, location, gender, and sort options (`CoachesController`).",
    "Handles availability slots and booking lifecycle operations (create, fetch, status update, delete).",
    "Runs auth flows with email OTP verification and JWT-based proof tokens (`AuthVerificationController`, JWT config in `Program.cs`).",
    "Supports Stripe billing flows: Connect onboarding, subscription checkout/cancel, gift checkout, and webhook processing (`BillingController`).",
    "Sends transactional emails for OTP, booking events, gifts, and subscription events (`SmtpEmailSender`, `BookingEmailFlows`, billing email handlers).",
    "Stores profile/gallery media in Azure Blob Storage and returns time-limited read URLs (`BlobStorageService`, `GalleryMediaController`).",
    "Creates and pushes in-app notifications via DB + SignalR hub groups (`NotificationService`, `/hubs/notify`).",
]
list_items = [ListItem(Paragraph(b, styles["BulletCompact"]), leftIndent=6) for b in feature_bullets]
story.append(ListFlowable(list_items, bulletType="bullet", start="-", leftIndent=10, bulletFontSize=7.5, spaceAfter=2))

story.append(Paragraph("How it works", styles["H2Compact"]))
arch_bullets = [
    "Ingress layer: ASP.NET Core controllers under `Controllers/` expose API routes; SignalR hub at `/hubs/notify` handles realtime events.",
    "Business/services layer: billing, subscriptions, notifications, email, and blob storage services are registered in DI (`Program.cs`, `Services/`).",
    "Data layer: `AppDbContext` maps domain entities (Coach, Booking, Availability, Review, Notification, Partner, Gift, etc.) to SQL Server via EF Core.",
    "Infra integrations: Stripe (payments/connect/webhooks), Azure Blob Storage (media), SMTP (email), Hangfire + SQL storage (background jobs/dashboard).",
    "Flow: HTTP request -> controller -> service/EF Core -> SQL/third-party integrations -> response; selected events also emit SignalR notifications and email side effects.",
]
arch_items = [ListItem(Paragraph(b, styles["BulletCompact"]), leftIndent=6) for b in arch_bullets]
story.append(ListFlowable(arch_items, bulletType="bullet", start="-", leftIndent=10, bulletFontSize=7.5, spaceAfter=2))

story.append(Paragraph("How to run", styles["H2Compact"]))
run_bullets = [
    "Prerequisites: .NET 8 SDK and SQL Server reachable by connection string `ConnectionStrings:mycon`.",
    "Set required configuration values (user-secrets, env vars, or appsettings): `ConnectionStrings:mycon`, `Stripe:SecretKey`, and `Jwt:Key` (startup fails fast if missing).",
    "Optional/feature-specific config: `Smtp:*`, `AzureStorage:*`, and Stripe plan/webhook settings for billing and media paths.",
    "From repo root: `dotnet restore` then `dotnet run --project PTfinder.API/PTfinder.API.csproj`.",
    "Open Swagger at `/swagger` on the launched URL (launch settings include `http://localhost:5197` and `https://localhost:7235`).",
]
run_items = [ListItem(Paragraph(b, styles["BulletCompact"]), leftIndent=6) for b in run_bullets]
story.append(ListFlowable(run_items, bulletType="bullet", start="-", leftIndent=10, bulletFontSize=7.5, spaceAfter=2))

story.append(Spacer(1, 2))
story.append(Paragraph("Not found in repo: explicit production deployment guide, frontend repository/link, and formal persona documentation.", styles["SmallNote"]))

def draw_header_footer(canvas, doc_obj):
    canvas.saveState()
    canvas.setFont("Helvetica", 7.2)
    canvas.setFillColor(colors.HexColor("#6B7280"))
    canvas.drawRightString(A4[0] - 15*mm, 8*mm, "PTfinder.API summary | generated from repository evidence")
    canvas.restoreState()

doc.build(story, onFirstPage=draw_header_footer)
print(out_path)
