using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QMSoft.Api.Features.Documents;

// ── Input DTOs ───────────────────────────────────────────────────────────────
// Deliberately entity-free: the controller maps Student/Exam/ExamResult rows
// into this shape, same separation the Node original had (routes/reportCards.js
// assembled `cardData`, services/reportCardPdf.js only ever rendered it).

public sealed record ReportCardSchoolInfo(string? Name, string? Address, string? City, string? State, string? Pincode);

public sealed record ReportCardStudentInfo(
    string? FirstName, string? LastName, string? AdmissionNo,
    string? Class, string? Section, string? RollNo, string? FatherName, string? MotherName);

/// <summary>Status is one of "absent" | "exempt" | "pass" | "fail" — mirrors the
/// Node original's row.status / row.isPassing branch (see drawSubjectsTable).</summary>
public sealed record ReportCardSubjectRow(
    string SubjectName, decimal MaxMarks, decimal? MarksObtained,
    decimal? Percentage, string? Grade, decimal? Gpa, string Status);

public sealed record ReportCardExamTotals(
    decimal TotalObtained, decimal TotalMax, decimal OverallPct, string? OverallGrade, decimal? OverallGpa);

public sealed record ReportCardExamSection(
    string ExamName, string ExamType, DateOnly? FromDate, DateOnly? ToDate,
    IReadOnlyList<ReportCardSubjectRow> Subjects, ReportCardExamTotals Totals);

public sealed record ReportCardCompositeSubject(string SubjectName, decimal FinalPercentage);

public sealed record ReportCardComposite(
    IReadOnlyList<ReportCardCompositeSubject> Subjects, decimal OverallFinalPercentage);

public sealed record ReportCardCoScholasticRow(string? Area, string? Grade, string? Remarks);

public sealed record ReportCardAttendanceSummary(int TotalDays, int PresentDays);

public sealed record ReportCardData(
    ReportCardSchoolInfo? School,
    ReportCardStudentInfo Student,
    string AcademicYear,
    IReadOnlyList<ReportCardExamSection> Exams,
    ReportCardComposite? Composite,
    IReadOnlyList<ReportCardCoScholasticRow>? CoScholastic,
    string? TeacherRemarks,
    ReportCardAttendanceSummary? AttendanceSummary);

public interface IPdfService
{
    byte[] GenerateReportCard(ReportCardData data);
}

/// <summary>
/// Port of services/reportCardPdf.js, using QuestPDF instead of PDFKit.
///
/// The Node version did manual y-coordinate bookkeeping and explicit
/// "if (y > 750) addPage()" checks throughout, because PDFKit has no layout
/// engine. QuestPDF does — Column/Table content flows and paginates on its
/// own, so all of that manual math is gone; the structure below matches the
/// Node original section-by-section, not line-by-line.
///
/// Register with builder.Services.AddScoped&lt;IPdfService, PdfService&gt;() when
/// wired back into Program.cs, alongside QuestPDF.Settings.License =
/// QuestPDF.Infrastructure.LicenseType.Community (must be set once at boot —
/// see the Program.cs Phase 4 comment).
/// </summary>
public sealed class PdfService : IPdfService
{
    // Colors.* returns QuestPDF's Color type, which has implicit conversions
    // to/from string. Declaring these fields as `string` made every ternary
    // mixing a field with a bare Colors.* value (e.g. Colors.Black,
    // Colors.White) ambiguous between the string and Color conversion paths —
    // CS0172. Typing the fields as Color removes the ambiguity outright.
    private static readonly Color Navy = Colors.Blue.Darken3;   // ≈ #1e3a8a
    private static readonly Color GraySmall = Colors.Grey.Darken1; // ≈ #555
    private static readonly Color GrayFaint = Colors.Grey.Lighten1; // ≈ #bbb
    private static readonly Color HeaderFill = Colors.Blue.Lighten5; // ≈ #e8edf8
    private static readonly Color StripeFill = Colors.Grey.Lighten4; // ≈ #f7f8fc
    private static readonly Color FailRed = Colors.Red.Darken2; // ≈ #b91c1c
    private static readonly Color BorderGray = Colors.Grey.Lighten1; // ≈ #ccc

    public byte[] GenerateReportCard(ReportCardData data)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    col.Spacing(2);

                    Header(col, data);
                    StudentInfo(col, data.Student);

                    foreach (var exam in data.Exams)
                        ExamSection(col, exam);

                    if (data.Composite is { } composite)
                        CompositeSection(col, composite);

                    if ((data.CoScholastic?.Count ?? 0) > 0 ||
                        !string.IsNullOrWhiteSpace(data.TeacherRemarks) ||
                        data.AttendanceSummary is not null)
                    {
                        CoScholasticSection(col, data.CoScholastic, data.TeacherRemarks, data.AttendanceSummary);
                    }

                    SignatureBlock(col);
                });

                page.Footer().AlignCenter().Text(
                    $"Generated: {DateTime.Now:dd MMM yyyy, h:mm tt}")
                    .FontSize(7).FontColor(GrayFaint);
            });
        });

        return doc.GeneratePdf();
    }

    // ── Sections ─────────────────────────────────────────────────────────

    private static void Header(ColumnDescriptor col, ReportCardData data)
    {
        var school = data.School;

        col.Item().AlignCenter().Text(school?.Name ?? "School Name")
            .FontSize(18).Bold().FontColor(Navy);

        var addressLine = string.Join(", ", new[] { school?.Address, school?.City, school?.State, school?.Pincode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (addressLine.Length > 0)
            col.Item().AlignCenter().Text(addressLine).FontSize(9).FontColor(GraySmall);

        col.Item().PaddingTop(4).AlignCenter().Text("REPORT CARD").FontSize(13).Bold();
        col.Item().AlignCenter().Text($"Academic Year: {data.AcademicYear}").FontSize(10).FontColor(GraySmall);

        Rule(col);
    }

    private static void StudentInfo(ColumnDescriptor col, ReportCardStudentInfo s)
    {
        col.Item().PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                LabelValue(c, "Name", $"{s.FirstName} {s.LastName}".Trim());
                LabelValue(c, "Admission No", s.AdmissionNo ?? "—");
                LabelValue(c, "Class", s.Section is { Length: > 0 } sec ? $"{s.Class} - {sec}" : s.Class ?? "—");
            });
            row.RelativeItem().Column(c =>
            {
                LabelValue(c, "Roll No", s.RollNo ?? "—");
                LabelValue(c, "Father", s.FatherName ?? "—");
                LabelValue(c, "Mother", s.MotherName ?? "—");
            });
        });

        Rule(col);
    }

    private static void LabelValue(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingBottom(3).Row(row =>
        {
            row.ConstantItem(95).Text($"{label}:").FontSize(9).Bold().FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).FontSize(9);
        });
    }

    private static void ExamSection(ColumnDescriptor col, ReportCardExamSection exam)
    {
        col.Item().PaddingTop(6).Text(exam.ExamName).FontSize(11).Bold().FontColor(Navy);

        var range = $"{FormatDate(exam.FromDate)} – {FormatDate(exam.ToDate)}";
        col.Item().Text($"{ExamTypeLabel(exam.ExamType)} · {range}").FontSize(9).FontColor(Colors.Grey.Darken1);

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3.7f);  // Subject
                c.RelativeColumn(0.9f);  // Max
                c.RelativeColumn(1.3f);  // Obtained
                c.RelativeColumn(1.0f);  // %
                c.RelativeColumn(1.1f);  // Grade
                c.RelativeColumn(0.9f);  // GPA
                c.RelativeColumn(1.4f);  // Status
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCellStyle).Text("Subject");
                header.Cell().Element(HeaderCellStyle).AlignRight().Text("Max");
                header.Cell().Element(HeaderCellStyle).AlignRight().Text("Obtained");
                header.Cell().Element(HeaderCellStyle).AlignRight().Text("%");
                header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Grade");
                header.Cell().Element(HeaderCellStyle).AlignRight().Text("GPA");
                header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Status");
            });

            var idx = 0;
            foreach (var r in exam.Subjects)
            {
                var isAbsent = r.Status == "absent";
                var statusLabel = r.Status switch
                {
                    "absent" => "Absent",
                    "exempt" => "Exempt",
                    "fail" => "Fail",
                    _ => "Pass",
                };
                var obtained = isAbsent ? "AB" : r.MarksObtained?.ToString() ?? "—";
                var pct = isAbsent ? "—" : r.Percentage is { } p ? $"{p}%" : "—";
                var textColor = r.Status == "fail" ? FailRed : Colors.Black;
                var fill = idx % 2 == 0 ? StripeFill : Colors.White;
                idx++;

                IContainer Style(IContainer c) => RowCellStyle(c, fill, textColor);

                table.Cell().Element(Style).Text(r.SubjectName);
                table.Cell().Element(Style).AlignRight().Text(r.MaxMarks.ToString());
                table.Cell().Element(Style).AlignRight().Text(obtained);
                table.Cell().Element(Style).AlignRight().Text(pct);
                table.Cell().Element(Style).AlignCenter().Text(r.Grade ?? "—");
                table.Cell().Element(Style).AlignRight().Text(r.Gpa?.ToString() ?? "—");
                table.Cell().Element(Style).AlignCenter().Text(statusLabel);
            }
        });

        var t = exam.Totals;
        var gradeStr = t.OverallGrade is { Length: > 0 } g ? $"   Grade: {g}" : "";
        var gpaStr = t.OverallGpa is { } gpa ? $"   GPA: {gpa}" : "";
        col.Item().PaddingTop(2).AlignRight().Text(
            $"Total: {t.TotalObtained} / {t.TotalMax}   |   Percentage: {t.OverallPct}%{gradeStr}{gpaStr}")
            .FontSize(10).Bold();
    }

    private static void CompositeSection(ColumnDescriptor col, ReportCardComposite composite)
    {
        Rule(col);
        col.Item().PaddingTop(4).AlignCenter().Text("Final Composite Result").FontSize(12).Bold().FontColor(Navy);
        col.Item().AlignCenter().Text("(Weighted aggregate of exams above)").FontSize(9).FontColor(Colors.Grey.Darken1);

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1.4f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCellStyle).Text("Subject");
                header.Cell().Element(HeaderCellStyle).AlignRight().Text("Final %");
            });

            var idx = 0;
            foreach (var s in composite.Subjects)
            {
                var fill = idx % 2 == 0 ? StripeFill : Colors.White;
                idx++;

                IContainer Style(IContainer c) => RowCellStyle(c, fill, Colors.Black);

                table.Cell().Element(Style).Text(s.SubjectName);
                table.Cell().Element(Style).AlignRight().Text($"{s.FinalPercentage}%");
            }
        });

        col.Item().PaddingTop(2).AlignRight().Text($"Final Percentage: {composite.OverallFinalPercentage}%")
            .FontSize(11).Bold();
    }

    private static void CoScholasticSection(
        ColumnDescriptor col, IReadOnlyList<ReportCardCoScholasticRow>? rows,
        string? teacherRemarks, ReportCardAttendanceSummary? attendance)
    {
        Rule(col);
        col.Item().PaddingTop(4).Text("Co-Scholastic Areas").FontSize(11).Bold().FontColor(Navy);

        if (attendance is { } a)
        {
            var pct = a.TotalDays > 0 ? Math.Round(a.PresentDays * 100.0 / a.TotalDays) : 0;
            col.Item().PaddingTop(3).Text($"Attendance: {a.PresentDays} / {a.TotalDays} days  ({pct}%)")
                .FontSize(9).FontColor(Colors.Grey.Darken2);
        }

        if (rows is { Count: > 0 })
        {
            col.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(1.35f);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).Text("Activity / Area");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Grade");
                    header.Cell().Element(HeaderCellStyle).Text("Remarks");
                });

                var idx = 0;
                foreach (var r in rows)
                {
                    var fill = idx % 2 == 0 ? StripeFill : Colors.White;
                    idx++;

                    IContainer Style(IContainer c) => RowCellStyle(c, fill, Colors.Black);

                    table.Cell().Element(Style).Text(r.Area ?? "");
                    table.Cell().Element(Style).AlignCenter().Text(r.Grade ?? "—");
                    table.Cell().Element(Style).Text(r.Remarks ?? "");
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(teacherRemarks))
        {
            col.Item().PaddingTop(4).Text(t =>
            {
                t.Span("Class Teacher's Remarks: ").FontSize(9).Bold().FontColor(GraySmall);
                t.Span(teacherRemarks).FontSize(9);
            });
        }
    }

    private static void SignatureBlock(ColumnDescriptor col)
    {
        col.Item().PaddingTop(24).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
        col.Item().PaddingTop(20).Row(row =>
        {
            SignatureLine(row, "Class Teacher");
            SignatureLine(row, "Principal");
            SignatureLine(row, "Parent / Guardian");
        });
    }

    private static void SignatureLine(RowDescriptor row, string label)
    {
        row.RelativeItem().Column(c =>
        {
            c.Item().PaddingHorizontal(10).LineHorizontal(0.8f).LineColor(Colors.Grey.Darken2);
            c.Item().PaddingTop(4).AlignCenter().Text(label).FontSize(8).FontColor(GraySmall);
        });
    }

    // ── Table cell styling helpers ──────────────────────────────────────
    // QuestPDF applies styling via .Element(Func&lt;IContainer,IContainer&gt;) before
    // .Text(...) — these are the shared style functions for header vs. body cells.

    private static IContainer HeaderCellStyle(IContainer container) =>
        container.Background(HeaderFill)
                  .PaddingVertical(5).PaddingHorizontal(4)
                  .DefaultTextStyle(x => x.FontSize(9).Bold().FontColor(Navy));

    private static IContainer RowCellStyle(IContainer container, Color fill, Color textColor) =>
        container.Background(fill)
                  .BorderBottom(0.5f).BorderColor(BorderGray)
                  .PaddingVertical(5).PaddingHorizontal(4)
                  .DefaultTextStyle(x => x.FontSize(9).FontColor(textColor));

    // ── Utilities ────────────────────────────────────────────────────────

    private static void Rule(ColumnDescriptor col) =>
        col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

    private static string FormatDate(DateOnly? d) =>
        d is { } date ? date.ToString("dd MMM yyyy") : "—";

    private static string ExamTypeLabel(string t) => t switch
    {
        "unit_test" => "Unit Test",
        "periodic" => "Periodic",
        "term" => "Term",
        "half_yearly" => "Half Yearly",
        "annual" => "Annual",
        "custom" => "Examination",
        _ => t,
    };
}
