using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QMSoft.Api.Features.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Report card PDF — QuestPDF port of the Node services/reportCardPdf.js layout.
//
// The Node original was 384 lines of PDFKit doing manual cursor arithmetic
// (doc.y bookkeeping, explicit column x offsets, addPage() at hard-coded y
// thresholds). QuestPDF's layout engine handles pagination and column widths,
// so this is shorter without losing anything — the visual structure is matched
// section for section:
//
//   header      school name (navy, centred) + address, "REPORT CARD",
//               academic year
//   student     two-column label/value block — name/admission/class on the
//               left, roll/father/mother on the right
//   per exam    name + type + date range, then a Subject/Max/Obtained/%/Grade/
//               GPA/Status table with zebra striping and failures in red,
//               then a right-aligned totals line
//   composite   weighted year-final table, when exams declare weightInFinal
//   extras      attendance summary, co-scholastic table, teacher remarks —
//               all optional (see note below)
//   signatures  Class Teacher | Principal | Parent / Guardian
//   footer      generation timestamp
//
// On the optional block: the Node PDF read cardData.coScholastic,
// .teacherRemarks and .attendanceSummary, but NO backend — Node or .NET — ever
// produced them. They are modelled here so the layout is ready, and the section
// is skipped entirely when empty, exactly as the original did. Populating them
// needs new columns; that is a schema decision, not a PDF one.
//
// Devanagari: the Node version registered a NotoSansDevanagari font so Hindi
// school names render. QuestPDF falls back per-glyph, so a Hindi name shows as
// boxes unless that font is registered at boot. See ReportCardPdf.Fonts below.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RcSubject(
    string SubjectName, decimal MaxMarks, decimal? MarksObtained,
    decimal? Percentage, string? Grade, decimal? Gpa, string Status, bool? IsPassing);

public sealed record RcExam(
    string Name, string Type, DateOnly? FromDate, DateOnly? ToDate,
    IReadOnlyList<RcSubject> Subjects,
    decimal TotalObtained, decimal TotalMax, decimal OverallPct, string? OverallGrade);

public sealed record RcCompositeSubject(string SubjectName, decimal FinalPercentage);

public sealed record RcComposite(
    decimal SumWeights, IReadOnlyList<RcCompositeSubject> Subjects, decimal OverallFinalPercentage);

/// <summary>Optional blocks — no backend populates these yet; see the note above.</summary>
public sealed record RcCoScholastic(string Area, string? Grade, string? Remarks);
public sealed record RcAttendance(int TotalDays, int PresentDays);

public sealed record ReportCardData(
    string SchoolName, string? SchoolAddressLine,
    string StudentName, string? AdmissionNo, string ClassLabel,
    string? RollNo, string? FatherName, string? MotherName,
    string AcademicYear,
    IReadOnlyList<RcExam> Exams,
    RcComposite? Composite,
    IReadOnlyList<RcCoScholastic>? CoScholastic = null,
    string? TeacherRemarks = null,
    RcAttendance? Attendance = null);

public static class ReportCardPdf
{
    // Matching the Node palette so the two outputs are recognisably the same doc.
    private const string Navy = "#1E3A8A";
    private const string HeadFill = "#E8EDF8";
    private const string ZebraFill = "#F7F8FC";
    private const string FailRed = "#B91C1C";
    private const string Muted = "#666666";
    private const string Faint = "#BBBBBB";
    private const string Rule = "#444444";

    private static string Dash(object? v) => v is null ? "—" : v.ToString() ?? "—";

    private static string ExamTypeLabel(string type) =>
        string.IsNullOrWhiteSpace(type) ? "" :
        string.Join(" ", type.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Dates(DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return "";
        var f = from?.ToString("dd MMM yyyy") ?? "";
        var t = to?.ToString("dd MMM yyyy") ?? "";
        return from is not null && to is not null ? $"{f} – {t}" : f + t;
    }

    public static byte[] Build(ReportCardData d) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(9));

            // ── Header ────────────────────────────────────────────────────
            page.Header().Column(col =>
            {
                col.Item().AlignCenter().Text(d.SchoolName)
                    .FontSize(18).Bold().FontColor(Navy);

                if (!string.IsNullOrWhiteSpace(d.SchoolAddressLine))
                    col.Item().AlignCenter().Text(d.SchoolAddressLine)
                        .FontSize(9).FontColor(Muted);

                col.Item().PaddingTop(6).AlignCenter().Text("REPORT CARD")
                    .FontSize(13).Bold();
                col.Item().AlignCenter().Text($"Academic Year: {d.AcademicYear}")
                    .FontSize(10).FontColor(Muted);

                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Rule);
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                // ── Student info: two label/value columns ─────────────────
                col.Item().PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        Info(c, "Name", d.StudentName);
                        Info(c, "Admission No", Dash(d.AdmissionNo));
                        Info(c, "Class", d.ClassLabel);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        Info(c, "Roll No", Dash(d.RollNo));
                        Info(c, "Father", Dash(d.FatherName));
                        Info(c, "Mother", Dash(d.MotherName));
                    });
                });

                col.Item().LineHorizontal(1).LineColor(Rule);

                // ── Per-exam tables ───────────────────────────────────────
                foreach (var ex in d.Exams)
                {
                    col.Item().PaddingTop(10).Text(ex.Name).FontSize(11).Bold().FontColor(Navy);

                    var meta = string.Join(" · ",
                        new[] { ExamTypeLabel(ex.Type), Dates(ex.FromDate, ex.ToDate) }
                            .Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (meta.Length > 0)
                        col.Item().Text(meta).FontSize(9).FontColor(Muted);

                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.4f);   // Subject
                            c.RelativeColumn(0.9f);   // Max
                            c.RelativeColumn(1.2f);   // Obtained
                            c.RelativeColumn(0.9f);   // %
                            c.RelativeColumn(1.0f);   // Grade
                            c.RelativeColumn(0.8f);   // GPA
                            c.RelativeColumn(1.2f);   // Status
                        });

                        t.Header(h =>
                        {
                            Th(h, "Subject", left: true);
                            Th(h, "Max"); Th(h, "Obtained"); Th(h, "%");
                            Th(h, "Grade", centre: true); Th(h, "GPA");
                            Th(h, "Status", centre: true);
                        });

                        var i = 0;
                        foreach (var s in ex.Subjects)
                        {
                            var status = s.Status == "absent" ? "Absent"
                                       : s.Status == "exempt" ? "Exempt"
                                       : s.Status == "not_entered" ? "—"
                                       : s.IsPassing == false ? "Fail"
                                       : "Pass";
                            var obtained = s.Status == "absent" ? "AB" : Dash(s.MarksObtained);
                            var pct = s.Status == "absent" ? "—"
                                    : s.Percentage is null ? "—" : $"{s.Percentage}%";

                            var zebra = i++ % 2 == 0;
                            var red = status == "Fail";

                            Td(t, s.SubjectName, zebra, red, left: true);
                            Td(t, s.MaxMarks.ToString(), zebra, red);
                            Td(t, obtained, zebra, red);
                            Td(t, pct, zebra, red);
                            Td(t, Dash(s.Grade), zebra, red, centre: true);
                            Td(t, Dash(s.Gpa), zebra, red);
                            Td(t, status, zebra, red, centre: true);
                        }
                    });

                    var gradePart = string.IsNullOrWhiteSpace(ex.OverallGrade)
                        ? "" : $"   Grade: {ex.OverallGrade}";
                    col.Item().PaddingTop(3).AlignRight()
                        .Text($"Total: {ex.TotalObtained} / {ex.TotalMax}   |   Percentage: {ex.OverallPct}%{gradePart}")
                        .FontSize(10).Bold();
                }

                // ── Composite (weighted year-final) ───────────────────────
                if (d.Composite is { Subjects.Count: > 0 } comp)
                {
                    col.Item().PaddingTop(14).Text("Final Result (Weighted)")
                        .FontSize(12).Bold().FontColor(Navy);
                    col.Item().Text($"Across exams totalling {comp.SumWeights}% weight")
                        .FontSize(9).FontColor(Muted);

                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1.4f); });
                        t.Header(h => { Th(h, "Subject", left: true); Th(h, "Final %"); });
                        var i = 0;
                        foreach (var s in comp.Subjects)
                        {
                            var zebra = i++ % 2 == 0;
                            Td(t, s.SubjectName, zebra, false, left: true);
                            Td(t, $"{s.FinalPercentage}%", zebra, false);
                        }
                    });

                    col.Item().PaddingTop(3).AlignRight()
                        .Text($"Overall: {comp.OverallFinalPercentage}%").FontSize(11).Bold();
                }

                // ── Optional: attendance / co-scholastic / remarks ────────
                var hasCo = d.CoScholastic is { Count: > 0 };
                if (d.Attendance is not null || hasCo || !string.IsNullOrWhiteSpace(d.TeacherRemarks))
                {
                    col.Item().PaddingTop(14).Text("Co-Scholastic & Attendance")
                        .FontSize(11).Bold().FontColor(Navy);

                    if (d.Attendance is { } att)
                    {
                        var pct = att.TotalDays > 0
                            ? (int)Math.Round(att.PresentDays * 100.0 / att.TotalDays) : 0;
                        col.Item().PaddingTop(2)
                            .Text($"Attendance: {att.PresentDays} / {att.TotalDays} days ({pct}%)")
                            .FontSize(9);
                    }

                    if (hasCo)
                    {
                        col.Item().PaddingTop(4).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            { c.RelativeColumn(2.6f); c.RelativeColumn(1f); c.RelativeColumn(3f); });
                            t.Header(h =>
                            { Th(h, "Area", left: true); Th(h, "Grade", centre: true); Th(h, "Remarks", left: true); });
                            var i = 0;
                            foreach (var c2 in d.CoScholastic!)
                            {
                                var zebra = i++ % 2 == 0;
                                Td(t, c2.Area, zebra, false, left: true);
                                Td(t, Dash(c2.Grade), zebra, false, centre: true);
                                Td(t, c2.Remarks ?? "", zebra, false, left: true);
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(d.TeacherRemarks))
                        col.Item().PaddingTop(6).Text(txt =>
                        {
                            txt.Span("Class Teacher's Remarks: ").Bold().FontColor(Muted);
                            txt.Span(d.TeacherRemarks);
                        });
                }

                // ── Signature block ───────────────────────────────────────
                col.Item().PaddingTop(30).LineHorizontal(1).LineColor(Rule);
                col.Item().PaddingTop(34).Row(row =>
                {
                    Sign(row, "Class Teacher");
                    row.ConstantItem(24);
                    Sign(row, "Principal");
                    row.ConstantItem(24);
                    Sign(row, "Parent / Guardian");
                });
            });

            page.Footer().AlignCenter()
                .Text($"Generated: {DateTime.UtcNow.AddHours(5.5):dd-MM-yyyy HH:mm}")
                .FontSize(7).FontColor(Faint);
        });
    }).GeneratePdf();

    private static void Info(ColumnDescriptor col, string label, string value) =>
        col.Item().PaddingVertical(1.5f).Row(r =>
        {
            r.ConstantItem(78).Text($"{label}:").Bold().FontColor("#333333");
            r.RelativeItem().Text(value);
        });

    private static void Th(TableCellDescriptor h, string label, bool left = false, bool centre = false)
    {
        // Alignment must be applied to the container BEFORE .Text() — calling
        // .AlignCenter() on the cell afterwards returns a new container that is
        // discarded, so the header silently stays left-aligned.
        var cell = h.Cell().Background(HeadFill).PaddingVertical(4).PaddingHorizontal(4);
        var container = left ? cell : centre ? cell.AlignCenter() : cell.AlignRight();
        container.Text(label).FontSize(9).Bold().FontColor(Navy);
    }

    private static void Td(TableDescriptor t, string value, bool zebra, bool red,
                           bool left = false, bool centre = false)
    {
        var cell = t.Cell().Background(zebra ? ZebraFill : Colors.White)
            .PaddingVertical(4).PaddingHorizontal(4);
        var container = left ? cell : centre ? cell.AlignCenter() : cell.AlignRight();
        container.Text(value).FontSize(9).FontColor(red ? FailRed : "#111111");
    }

    private static void Sign(RowDescriptor row, string label) =>
        row.RelativeItem().Column(c =>
        {
            c.Item().LineHorizontal(0.8f).LineColor(Rule);
            c.Item().PaddingTop(4).AlignCenter().Text(label).FontSize(8).FontColor(Muted);
        });
}
