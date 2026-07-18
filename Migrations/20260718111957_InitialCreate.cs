using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using QMSoft.Api.Domain.Entities;

#nullable disable

namespace QMSoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:attendance_mode", "daily,period")
                .Annotation("Npgsql:Enum:attendance_status", "present,absent,late,leave,holiday")
                .Annotation("Npgsql:Enum:exam_result_status", "absent,present,exempt")
                .Annotation("Npgsql:Enum:exam_status", "draft,scheduled,in_progress,completed,published")
                .Annotation("Npgsql:Enum:exam_type", "unit_test,periodic,term,half_yearly,annual,custom")
                .Annotation("Npgsql:Enum:fee_frequency", "one_time,monthly,quarterly,half_yearly,annual")
                .Annotation("Npgsql:Enum:gender", "male,female,other")
                .Annotation("Npgsql:Enum:grading_scale_type", "marks,grade,gpa,pass_fail")
                .Annotation("Npgsql:Enum:invoice_status", "pending,partial,paid,overdue,cancelled")
                .Annotation("Npgsql:Enum:leave_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:payment_method", "cash,cheque,upi,card,bank_transfer,razorpay")
                .Annotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded")
                .Annotation("Npgsql:Enum:payroll_run_status", "draft,generated,locked,transfer_queued,transfer_completed,cancelled")
                .Annotation("Npgsql:Enum:payroll_status", "draft,generated,locked,paid,failed")
                .Annotation("Npgsql:Enum:poll_category", "satisfaction,event,canteen,general")
                .Annotation("Npgsql:Enum:poll_status", "draft,active,closed")
                .Annotation("Npgsql:Enum:religion", "Hindu,Muslim,Sikh,Christian,Buddhist,Jain,Other")
                .Annotation("Npgsql:Enum:school_plan", "trial,basic,pro,enterprise")
                .Annotation("Npgsql:Enum:school_type", "k12,coaching,college,other")
                .Annotation("Npgsql:Enum:sibling_relation", "brother,sister")
                .Annotation("Npgsql:Enum:student_category", "GEN,OBC,SC,ST,EWS")
                .Annotation("Npgsql:Enum:student_status", "active,inactive,transferred,graduated")
                .Annotation("Npgsql:Enum:teacher_attendance_status", "present,absent,half_day,leave,unpaid_leave,on_duty,holiday")
                .Annotation("Npgsql:Enum:timetable_status", "draft,active,archived")
                .Annotation("Npgsql:Enum:transport_mode", "self,school_bus,walk,other")
                .Annotation("Npgsql:Enum:user_role", "superadmin,school_admin,principal,accountant,teacher,parent,student")
                .Annotation("Npgsql:Enum:variation_type", "substitute_teacher,cancelled,rescheduled,guest_lecture,custom")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Entity = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Meta = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fee_structures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "INR"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUsername = table.Column<string>(type: "text", nullable: true),
                    LastEditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEditedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastEditedByUsername = table.Column<string>(type: "text", nullable: true),
                    EditHistory = table.Column<string>(type: "jsonb", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_structures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grading_scales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<GradingScaleType>(type: "grading_scale_type", nullable: false),
                    PassingMark = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 33m),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grading_scales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: true),
                    TotalTeachers = table.Column<int>(type: "integer", nullable: false),
                    PayrollsGenerated = table.Column<int>(type: "integer", nullable: false),
                    PayrollsSkipped = table.Column<int>(type: "integer", nullable: false),
                    PayrollsLocked = table.Column<int>(type: "integer", nullable: false),
                    PayrollsPaid = table.Column<int>(type: "integer", nullable: false),
                    PayrollsFailed = table.Column<int>(type: "integer", nullable: false),
                    TotalGross = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Skipped = table.Column<string>(type: "jsonb", nullable: false),
                    RazorpayBatchId = table.Column<string>(type: "text", nullable: true),
                    BatchTransferId = table.Column<string>(type: "text", nullable: true),
                    BatchStatus = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<PayrollRunStatus>(type: "payroll_run_status", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_runs", x => x.Id);
                    table.CheckConstraint("ck_payroll_runs_period", "month BETWEEN 1 AND 12 AND year BETWEEN 2000 AND 2100");
                });

            migrationBuilder.CreateTable(
                name: "role_privileges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Privilege = table.Column<string>(type: "text", nullable: false),
                    Roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_privileges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "schools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameHindi = table.Column<string>(type: "text", nullable: true),
                    Slug = table.Column<string>(type: "citext", nullable: false),
                    type = table.Column<SchoolType>(type: "school_type", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Pincode = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "citext", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryColor = table.Column<string>(type: "text", nullable: false, defaultValue: "#1e40af"),
                    AcademicYear = table.Column<string>(type: "text", nullable: false, defaultValue: "2025-2026"),
                    AcademicYearStartMonth = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    Classes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Sections = table.Column<List<string>>(type: "text[]", nullable: false),
                    WorkingDays = table.Column<List<string>>(type: "text[]", nullable: false),
                    LeaveRequireApproval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FeeBillingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FeeBillingDay = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FeeReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FeeReminderDay = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    plan = table.Column<SchoolPlan>(type: "school_plan", nullable: false),
                    PlanExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RazorpayKeyId = table.Column<string>(type: "text", nullable: true),
                    RazorpayKeySecret = table.Column<string>(type: "text", nullable: true),
                    PaymentVpa = table.Column<string>(type: "text", nullable: true),
                    PaymentPayeeName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schools", x => x.Id);
                    table.CheckConstraint("ck_schools_ay_start_month", "academic_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_schools_fee_days", "fee_billing_day BETWEEN 1 AND 28 AND fee_reminder_day BETWEEN 1 AND 28");
                    table.CheckConstraint("ck_schools_working_days", "working_days <@ ARRAY['Mon','Tue','Wed','Thu','Fri','Sat','Sun']::text[]");
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<string>(type: "text", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    IsCoScholastic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DefaultMaxMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: false, defaultValue: 100m),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "time_slots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SlotNumber = table.Column<int>(type: "integer", nullable: false),
                    DefaultStartTime = table.Column<string>(type: "text", nullable: false),
                    DefaultEndTime = table.Column<string>(type: "text", nullable: false),
                    DayTimes = table.Column<string>(type: "jsonb", nullable: false),
                    SpansMultiplePeriods = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    NextSlotNumber = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_slots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timetables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Term = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<TimetableStatus>(type: "timetable_status", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timetables", x => x.Id);
                    table.CheckConstraint("ck_timetables_dates", "to_date IS NULL OR to_date >= from_date");
                });

            migrationBuilder.CreateTable(
                name: "fee_heads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    FeeStructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    frequency = table.Column<FeeFrequency>(type: "fee_frequency", nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_heads", x => x.Id);
                    table.CheckConstraint("ck_fee_heads_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_fee_heads_fee_structures_FeeStructureId",
                        column: x => x.FeeStructureId,
                        principalTable: "fee_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fee_installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    FeeStructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_installments", x => x.Id);
                    table.CheckConstraint("ck_fee_installments_pct", "percentage > 0 AND percentage <= 100");
                    table.ForeignKey(
                        name: "FK_fee_installments_fee_structures_FeeStructureId",
                        column: x => x.FeeStructureId,
                        principalTable: "fee_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<ExamType>(type: "exam_type", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GradingScaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    WeightInFinal = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<ExamStatus>(type: "exam_status", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exams", x => x.Id);
                    table.CheckConstraint("ck_exams_dates", "to_date >= from_date");
                    table.CheckConstraint("ck_exams_weight", "weight_in_final BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_exams_grading_scales_GradingScaleId",
                        column: x => x.GradingScaleId,
                        principalTable: "grading_scales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grading_bands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    GradingScaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    MaxPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Grade = table.Column<string>(type: "text", nullable: false),
                    Gpa = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsPassing = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grading_bands", x => x.Id);
                    table.CheckConstraint("ck_grading_bands_range", "min_percent BETWEEN 0 AND 100 AND max_percent BETWEEN 0 AND 100 AND max_percent >= min_percent AND (gpa IS NULL OR gpa BETWEEN 0 AND 10)");
                    table.ForeignKey(
                        name: "FK_grading_bands_grading_scales_GradingScaleId",
                        column: x => x.GradingScaleId,
                        principalTable: "grading_scales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "school_leave_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TotalDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_leave_types", x => x.Id);
                    table.CheckConstraint("ck_school_leave_types_days", "total_days >= 0");
                    table.ForeignKey(
                        name: "FK_school_leave_types_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AdmissionNo = table.Column<string>(type: "text", nullable: false),
                    RollNo = table.Column<string>(type: "text", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    FirstNameHi = table.Column<string>(type: "text", nullable: true),
                    LastNameHi = table.Column<string>(type: "text", nullable: true),
                    Dob = table.Column<DateOnly>(type: "date", nullable: true),
                    Gender = table.Column<Gender>(type: "gender", nullable: true),
                    BloodGroup = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    AdmissionDate = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Pincode = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "citext", nullable: true),
                    FatherName = table.Column<string>(type: "text", nullable: true),
                    FatherNameHi = table.Column<string>(type: "text", nullable: true),
                    FatherPhone = table.Column<string>(type: "text", nullable: true),
                    FatherOccup = table.Column<string>(type: "text", nullable: true),
                    MotherName = table.Column<string>(type: "text", nullable: true),
                    MotherNameHi = table.Column<string>(type: "text", nullable: true),
                    MotherPhone = table.Column<string>(type: "text", nullable: true),
                    MotherOccup = table.Column<string>(type: "text", nullable: true),
                    GuardianName = table.Column<string>(type: "text", nullable: true),
                    GuardianPhone = table.Column<string>(type: "text", nullable: true),
                    GuardianRel = table.Column<string>(type: "text", nullable: true),
                    AadharNo = table.Column<string>(type: "text", nullable: true),
                    AadharDoc = table.Column<string>(type: "text", nullable: true),
                    AadharDocKey = table.Column<string>(type: "text", nullable: true),
                    BirthCertNo = table.Column<string>(type: "text", nullable: true),
                    BirthDoc = table.Column<string>(type: "text", nullable: true),
                    BirthDocKey = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<StudentCategory>(type: "student_category", nullable: true),
                    Caste = table.Column<string>(type: "text", nullable: true),
                    Religion = table.Column<Religion>(type: "religion", nullable: true),
                    MotherTongue = table.Column<string>(type: "text", nullable: true),
                    Nationality = table.Column<string>(type: "text", nullable: false, defaultValue: "Indian"),
                    TransportMode = table.Column<TransportMode>(type: "transport_mode", nullable: true),
                    BusRoute = table.Column<string>(type: "text", nullable: true),
                    PickupPoint = table.Column<string>(type: "text", nullable: true),
                    PrevSchool = table.Column<string>(type: "text", nullable: true),
                    PrevClass = table.Column<string>(type: "text", nullable: true),
                    TcNo = table.Column<string>(type: "text", nullable: true),
                    TcDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TcDoc = table.Column<string>(type: "text", nullable: true),
                    TcDocKey = table.Column<string>(type: "text", nullable: true),
                    House = table.Column<string>(type: "text", nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ShareToken = table.Column<string>(type: "text", nullable: true),
                    ShareEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<StudentStatus>(type: "student_status", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_students", x => x.Id);
                    table.ForeignKey(
                        name: "FK_students_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectName = table.Column<string>(type: "text", nullable: false),
                    MaxMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    PassingMark = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    TheoryMax = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    PracticalMax = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartTime = table.Column<string>(type: "text", nullable: true),
                    DurationMins = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exam_subjects", x => x.Id);
                    table.CheckConstraint("ck_exam_subjects_marks", "max_marks >= 0 AND (theory_max IS NULL OR theory_max >= 0) AND (practical_max IS NULL OR practical_max >= 0)");
                    table.ForeignKey(
                        name: "FK_exam_subjects_exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exam_subjects_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeStructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceNo = table.Column<string>(type: "text", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: true),
                    StudentAdmNo = table.Column<string>(type: "text", nullable: true),
                    StudentClass = table.Column<string>(type: "text", nullable: true),
                    StudentSection = table.Column<string>(type: "text", nullable: true),
                    InstallmentName = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    DiscountReason = table.Column<string>(type: "text", nullable: true),
                    LateFee = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    Total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<InvoiceStatus>(type: "invoice_status", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(12,2)", nullable: false, computedColumnSql: "GREATEST(0::numeric, total - amount_paid)", stored: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_invoices", x => x.Id);
                    table.CheckConstraint("ck_fee_invoices_amounts", "subtotal >= 0 AND discount >= 0 AND late_fee >= 0 AND total >= 0 AND amount_paid >= 0");
                    table.CheckConstraint("ck_fee_invoices_no_stored_overdue", "status <> 'overdue'");
                    table.ForeignKey(
                        name: "FK_fee_invoices_fee_structures_FeeStructureId",
                        column: x => x.FeeStructureId,
                        principalTable: "fee_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fee_invoices_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_passed_exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamName = table.Column<string>(type: "text", nullable: true),
                    Institution = table.Column<string>(type: "text", nullable: true),
                    Year = table.Column<string>(type: "text", nullable: true),
                    RollNo = table.Column<string>(type: "text", nullable: true),
                    Board = table.Column<string>(type: "text", nullable: true),
                    MaxMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    ObtainedMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_passed_exams", x => x.Id);
                    table.CheckConstraint("ck_passed_exams_marks", "(max_marks IS NULL OR max_marks >= 0) AND (obtained_marks IS NULL OR obtained_marks >= 0)");
                    table.ForeignKey(
                        name: "FK_student_passed_exams_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_siblings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<string>(type: "text", nullable: true),
                    relation = table.Column<SiblingRelation>(type: "sibling_relation", nullable: true),
                    SameSchool = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_siblings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_student_siblings_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolSlug = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "citext", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<UserRole>(type: "user_role", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "citext", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_users_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeadName = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_invoice_lines", x => x.Id);
                    table.CheckConstraint("ck_fee_invoice_lines_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_fee_invoice_lines_fee_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "fee_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    mode = table.Column<AttendanceMode>(type: "attendance_mode", nullable: false),
                    Period = table.Column<short>(type: "smallint", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<AttendanceStatus>(type: "attendance_status", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaveReason = table.Column<string>(type: "text", nullable: true),
                    MarkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkedByName = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance", x => x.Id);
                    table.CheckConstraint("ck_attendance_period_mode", "(mode = 'period') = (period IS NOT NULL) AND (period IS NULL OR period BETWEEN 1 AND 12)");
                    table.ForeignKey(
                        name: "FK_attendance_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_users_MarkedByUserId",
                        column: x => x.MarkedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "class_teachers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_teachers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_teachers_users_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamName = table.Column<string>(type: "text", nullable: true),
                    SubjectName = table.Column<string>(type: "text", nullable: true),
                    StudentName = table.Column<string>(type: "text", nullable: true),
                    StudentAdmNo = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<string>(type: "text", nullable: true),
                    Section = table.Column<string>(type: "text", nullable: true),
                    AcademicYear = table.Column<string>(type: "text", nullable: true),
                    MarksObtained = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    MaxMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    TheoryMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    PracticalMarks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    Percentage = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    Grade = table.Column<string>(type: "text", nullable: true),
                    Gpa = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    IsPassing = table.Column<bool>(type: "boolean", nullable: true),
                    status = table.Column<ExamResultStatus>(type: "exam_result_status", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    EnteredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnteredByName = table.Column<string>(type: "text", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exam_results", x => x.Id);
                    table.CheckConstraint("ck_exam_results_absent", "status <> 'absent' OR marks_obtained IS NULL");
                    table.CheckConstraint("ck_exam_results_ranges", "(marks_obtained IS NULL OR marks_obtained >= 0) AND max_marks >= 0 AND (percentage IS NULL OR percentage BETWEEN 0 AND 100) AND (gpa IS NULL OR gpa BETWEEN 0 AND 10)");
                    table.ForeignKey(
                        name: "FK_exam_results_exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exam_results_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_results_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_results_users_EnteredByUserId",
                        column: x => x.EnteredByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leaves_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNo = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    method = table.Column<PaymentMethod>(type: "payment_method", nullable: false),
                    RazorpayOrderId = table.Column<string>(type: "text", nullable: true),
                    RazorpayPaymentId = table.Column<string>(type: "text", nullable: true),
                    RazorpayVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ChequeNo = table.Column<string>(type: "text", nullable: true),
                    ChequeBank = table.Column<string>(type: "text", nullable: true),
                    ChequeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionRef = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<PaymentStatus>(type: "payment_status", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CollectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payments_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_payments_fee_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "fee_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_users_CollectedByUserId",
                        column: x => x.CollectedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "polls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<PollCategory>(type: "poll_category", nullable: false),
                    TargetRoles = table.Column<List<string>>(type: "text[]", nullable: false),
                    status = table.Column<PollStatus>(type: "poll_status", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShowResultsBeforeClose = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AllowAnonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_polls", x => x.Id);
                    table.CheckConstraint("ck_polls_target_roles", "target_roles <@ ARRAY['parent','teacher','student','school_admin','principal','accountant']::text[]");
                    table.ForeignKey(
                        name: "FK_polls_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "salary_structures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Da = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    Hra = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    Ta = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    OtherAllowances = table.Column<string>(type: "jsonb", nullable: false),
                    Pf = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    Esi = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    ProfessionalTax = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    IncomeTax = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    OtherDeductions = table.Column<string>(type: "jsonb", nullable: false),
                    LeaveDeductionPerDay = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    BankAccountNumber = table.Column<string>(type: "text", nullable: true),
                    BankIfsc = table.Column<string>(type: "text", nullable: true),
                    BankAccountHolder = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_salary_structures", x => x.Id);
                    table.CheckConstraint("ck_salary_structures_pcts", "base_salary >= 0 AND da BETWEEN 0 AND 200 AND hra BETWEEN 0 AND 200 AND ta >= 0 AND pf BETWEEN 0 AND 100 AND esi BETWEEN 0 AND 100 AND professional_tax >= 0 AND income_tax BETWEEN 0 AND 100 AND leave_deduction_per_day >= 0");
                    table.ForeignKey(
                        name: "FK_salary_structures_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<TeacherAttendanceStatus>(type: "teacher_attendance_status", nullable: false),
                    CheckIn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OnDutyNote = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    TeacherName = table.Column<string>(type: "text", nullable: true),
                    MarkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkedByName = table.Column<string>(type: "text", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_attendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teacher_attendance_users_MarkedByUserId",
                        column: x => x.MarkedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_teacher_attendance_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timetable_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TimetableId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    SlotNumber = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherName = table.Column<string>(type: "text", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectName = table.Column<string>(type: "text", nullable: true),
                    Room = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timetable_entries", x => x.Id);
                    table.CheckConstraint("ck_timetable_entries_dow", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_timetable_entries_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_timetable_entries_timetables_TimetableId",
                        column: x => x.TimetableId,
                        principalTable: "timetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_timetable_entries_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timetable_variations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TimetableId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayOfWeek = table.Column<short>(type: "smallint", nullable: false),
                    SlotNumber = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<VariationType>(type: "variation_type", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeacherName = table.Column<string>(type: "text", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectName = table.Column<string>(type: "text", nullable: true),
                    Room = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timetable_variations", x => x.Id);
                    table.CheckConstraint("ck_timetable_variations_dow", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_timetable_variations_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_timetable_variations_timetables_TimetableId",
                        column: x => x.TimetableId,
                        principalTable: "timetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_timetable_variations_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_timetable_variations_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_parent_of",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_parent_of", x => new { x.user_id, x.student_id });
                    table.ForeignKey(
                        name: "FK_user_parent_of_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_parent_of_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LeaveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TotalDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    UsedDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    BalanceDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false, computedColumnSql: "total_days - used_days", stored: true),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_types", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leave_types_leaves_LeaveId",
                        column: x => x.LeaveId,
                        principalTable: "leaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_poll_questions_polls_PollId",
                        column: x => x.PollId,
                        principalTable: "polls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    UserRole = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_poll_votes_polls_PollId",
                        column: x => x.PollId,
                        principalTable: "polls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_poll_votes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payrolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalaryStructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    AcademicYear = table.Column<string>(type: "text", nullable: true),
                    TeacherName = table.Column<string>(type: "text", nullable: true),
                    TeacherEmail = table.Column<string>(type: "text", nullable: true),
                    TeacherUsername = table.Column<string>(type: "text", nullable: true),
                    BankAccount = table.Column<string>(type: "text", nullable: true),
                    BankIfsc = table.Column<string>(type: "text", nullable: true),
                    BankAccountHolder = table.Column<string>(type: "text", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Da = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Hra = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Ta = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OtherAllowances = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OtherAllowanceItems = table.Column<string>(type: "jsonb", nullable: false),
                    GrossSalary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Pf = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Esi = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ProfessionalTax = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    IncomeTax = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    LeaveDeduction = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OtherDeductionItems = table.Column<string>(type: "jsonb", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    NetSalary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    UnpaidLeaveDays = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    DailyRate = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<PayrollStatus>(type: "payroll_status", nullable: false),
                    RazorpayTransferId = table.Column<string>(type: "text", nullable: true),
                    TransferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransferStatus = table.Column<string>(type: "text", nullable: true),
                    TransferFailureReason = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payrolls", x => x.Id);
                    table.CheckConstraint("ck_payrolls_nonneg", "base_salary >= 0 AND gross_salary >= 0 AND total_deductions >= 0 AND net_salary >= 0 AND unpaid_leave_days >= 0 AND daily_rate >= 0");
                    table.CheckConstraint("ck_payrolls_period", "month BETWEEN 1 AND 12 AND year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_payrolls_payroll_runs_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "payroll_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payrolls_salary_structures_SalaryStructureId",
                        column: x => x.SalaryStructureId,
                        principalTable: "salary_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payrolls_users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LeaveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<LeaveStatus>(type: "leave_status", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_records", x => x.Id);
                    table.CheckConstraint("ck_leave_records_dates", "to_date >= from_date");
                    table.ForeignKey(
                        name: "FK_leave_records_leave_types_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "leave_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_leave_records_leaves_LeaveId",
                        column: x => x.LeaveId,
                        principalTable: "leaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_poll_options_poll_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "poll_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_vote_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    VoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_vote_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_poll_vote_answers_poll_options_OptionId",
                        column: x => x.OptionId,
                        principalTable: "poll_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_poll_vote_answers_poll_questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "poll_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_poll_vote_answers_poll_votes_VoteId",
                        column: x => x.VoteId,
                        principalTable: "poll_votes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_MarkedByUserId",
                table: "attendance",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_roster",
                table: "attendance",
                columns: new[] { "SchoolId", "Class", "Section", "Date", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_student_history",
                table: "attendance",
                columns: new[] { "SchoolId", "StudentId", "Date" },
                unique: true,
                descending: new[] { false, false, true },
                filter: "mode = 'daily'");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_StudentId",
                table: "attendance",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "uq_attendance_period",
                table: "attendance",
                columns: new[] { "SchoolId", "StudentId", "Date", "Period", "Subject" },
                unique: true,
                filter: "mode = 'period'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_meta",
                table: "audit_logs",
                column: "Meta")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_school_time",
                table: "audit_logs",
                columns: new[] { "SchoolId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_class_teachers_by_teacher",
                table: "class_teachers",
                columns: new[] { "SchoolId", "TeacherUserId", "AcademicYear" });

            migrationBuilder.CreateIndex(
                name: "IX_class_teachers_TeacherUserId",
                table: "class_teachers",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "uq_class_teachers_assignment",
                table: "class_teachers",
                columns: new[] { "SchoolId", "AcademicYear", "Class", "Section", "Subject", "TeacherUserId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_EnteredByUserId",
                table: "exam_results",
                column: "EnteredByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_exam_class",
                table: "exam_results",
                columns: new[] { "SchoolId", "ExamId", "Class", "Section" });

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_ExamId",
                table: "exam_results",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_student_year",
                table: "exam_results",
                columns: new[] { "SchoolId", "StudentId", "AcademicYear" });

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_StudentId",
                table: "exam_results",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_SubjectId",
                table: "exam_results",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "uq_exam_results_key",
                table: "exam_results",
                columns: new[] { "SchoolId", "ExamId", "StudentId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exam_subjects_SubjectId",
                table: "exam_subjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "uq_exam_subjects_exam_subject",
                table: "exam_subjects",
                columns: new[] { "ExamId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exams_GradingScaleId",
                table: "exams",
                column: "GradingScaleId");

            migrationBuilder.CreateIndex(
                name: "ix_exams_lookup",
                table: "exams",
                columns: new[] { "SchoolId", "AcademicYear", "Class", "Section", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_fee_heads_FeeStructureId",
                table: "fee_heads",
                column: "FeeStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_fee_installments_FeeStructureId",
                table: "fee_installments",
                column: "FeeStructureId");

            migrationBuilder.CreateIndex(
                name: "uq_fee_installments_name",
                table: "fee_installments",
                columns: new[] { "FeeStructureId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoice_lines_head",
                table: "fee_invoice_lines",
                column: "HeadName");

            migrationBuilder.CreateIndex(
                name: "IX_fee_invoice_lines_InvoiceId",
                table: "fee_invoice_lines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_due_status",
                table: "fee_invoices",
                columns: new[] { "SchoolId", "DueDate", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fee_invoices_FeeStructureId",
                table: "fee_invoices",
                column: "FeeStructureId");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_student_year",
                table: "fee_invoices",
                columns: new[] { "SchoolId", "StudentId", "AcademicYear" });

            migrationBuilder.CreateIndex(
                name: "IX_fee_invoices_StudentId",
                table: "fee_invoices",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "uq_fee_invoices_school_no",
                table: "fee_invoices",
                columns: new[] { "SchoolId", "InvoiceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_structures_lookup",
                table: "fee_structures",
                columns: new[] { "SchoolId", "AcademicYear", "Class", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_grading_bands_GradingScaleId",
                table: "grading_bands",
                column: "GradingScaleId");

            migrationBuilder.CreateIndex(
                name: "uq_grading_scales_one_default",
                table: "grading_scales",
                column: "SchoolId",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "uq_grading_scales_school_name",
                table: "grading_scales",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_records_LeaveId",
                table: "leave_records",
                column: "LeaveId");

            migrationBuilder.CreateIndex(
                name: "IX_leave_records_LeaveTypeId",
                table: "leave_records",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "uq_leave_types_name",
                table: "leave_types",
                columns: new[] { "LeaveId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leaves_TeacherId",
                table: "leaves",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "uq_leaves_teacher_year",
                table: "leaves",
                columns: new[] { "SchoolId", "TeacherId", "AcademicYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_CollectedByUserId",
                table: "payments",
                column: "CollectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_InvoiceId",
                table: "payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid",
                table: "payments",
                columns: new[] { "SchoolId", "PaidAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_rzp_order",
                table: "payments",
                column: "RazorpayOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_StudentId",
                table: "payments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "uq_payments_idem",
                table: "payments",
                columns: new[] { "SchoolId", "InvoiceId", "IdempotencyKey" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_receipt",
                table: "payments",
                columns: new[] { "SchoolId", "ReceiptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_payments_rzp",
                table: "payments",
                column: "RazorpayPaymentId",
                unique: true,
                filter: "razorpay_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payroll_runs_period",
                table: "payroll_runs",
                columns: new[] { "SchoolId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payrolls_PayrollRunId",
                table: "payrolls",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_run",
                table: "payrolls",
                columns: new[] { "SchoolId", "PayrollRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_payrolls_SalaryStructureId",
                table: "payrolls",
                column: "SalaryStructureId");

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_status_period",
                table: "payrolls",
                columns: new[] { "SchoolId", "status", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_payrolls_TeacherId",
                table: "payrolls",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "uq_payrolls_teacher_period",
                table: "payrolls",
                columns: new[] { "SchoolId", "TeacherId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_poll_options_QuestionId",
                table: "poll_options",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_poll_questions_PollId",
                table: "poll_questions",
                column: "PollId");

            migrationBuilder.CreateIndex(
                name: "ix_poll_vote_answers_option",
                table: "poll_vote_answers",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_poll_vote_answers_QuestionId",
                table: "poll_vote_answers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "uq_poll_vote_answers_question",
                table: "poll_vote_answers",
                columns: new[] { "VoteId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_poll_votes_school",
                table: "poll_votes",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_poll_votes_UserId",
                table: "poll_votes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "uq_poll_votes_one_per_user",
                table: "poll_votes",
                columns: new[] { "PollId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_polls_CreatedByUserId",
                table: "polls",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_polls_school_status",
                table: "polls",
                columns: new[] { "SchoolId", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_role_privileges_roles",
                table: "role_privileges",
                column: "Roles")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "uq_role_privileges_school_priv",
                table: "role_privileges",
                columns: new[] { "SchoolId", "Privilege" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_salary_structures_TeacherId",
                table: "salary_structures",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "uq_salary_structures_teacher_year",
                table: "salary_structures",
                columns: new[] { "SchoolId", "TeacherId", "AcademicYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_school_leave_types_SchoolId_Name",
                table: "school_leave_types",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schools_Slug",
                table: "schools",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_passed_exams_StudentId",
                table: "student_passed_exams",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_student_siblings_StudentId",
                table: "student_siblings",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "ix_students_admno_trgm",
                table: "students",
                column: "AdmissionNo")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_deleted",
                table: "students",
                column: "IsDeleted",
                filter: "is_deleted = true");

            migrationBuilder.CreateIndex(
                name: "ix_students_firstname_trgm",
                table: "students",
                column: "FirstName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_lastname_trgm",
                table: "students",
                column: "LastName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_school_class_section",
                table: "students",
                columns: new[] { "SchoolId", "Class", "Section" });

            migrationBuilder.CreateIndex(
                name: "ix_students_share_token",
                table: "students",
                column: "ShareToken",
                filter: "share_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_students_school_admission",
                table: "students",
                columns: new[] { "SchoolId", "AdmissionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_subjects_school_year_class_name",
                table: "subjects",
                columns: new[] { "SchoolId", "AcademicYear", "Class", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_history",
                table: "teacher_attendance",
                columns: new[] { "SchoolId", "TeacherId", "Date" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_attendance_MarkedByUserId",
                table: "teacher_attendance",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_roster",
                table: "teacher_attendance",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_attendance_TeacherId",
                table: "teacher_attendance",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "uq_time_slots_school_number",
                table: "time_slots",
                columns: new[] { "SchoolId", "SlotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_grid",
                table: "timetable_entries",
                columns: new[] { "TimetableId", "DayOfWeek", "SlotNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_timetable_entries_SubjectId",
                table: "timetable_entries",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_teacher",
                table: "timetable_entries",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_timetable_variations_CreatedByUserId",
                table: "timetable_variations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_date",
                table: "timetable_variations",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_timetable_variations_SubjectId",
                table: "timetable_variations",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_timetable_variations_TeacherId",
                table: "timetable_variations",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_timetable_variations_TimetableId",
                table: "timetable_variations",
                column: "TimetableId");

            migrationBuilder.CreateIndex(
                name: "uq_timetable_variations_key",
                table: "timetable_variations",
                columns: new[] { "SchoolId", "TimetableId", "Date", "SlotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timetables_lookup",
                table: "timetables",
                columns: new[] { "SchoolId", "Class", "Section", "AcademicYear", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_user_parent_of_student_id",
                table: "user_parent_of",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_expiry",
                table: "user_refresh_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_hash",
                table: "user_refresh_tokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_user",
                table: "user_refresh_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_StudentId",
                table: "users",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "uq_users_global_username",
                table: "users",
                column: "Username",
                unique: true,
                filter: "school_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_users_school_username",
                table: "users",
                columns: new[] { "SchoolId", "Username" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "class_teachers");

            migrationBuilder.DropTable(
                name: "exam_results");

            migrationBuilder.DropTable(
                name: "exam_subjects");

            migrationBuilder.DropTable(
                name: "fee_heads");

            migrationBuilder.DropTable(
                name: "fee_installments");

            migrationBuilder.DropTable(
                name: "fee_invoice_lines");

            migrationBuilder.DropTable(
                name: "grading_bands");

            migrationBuilder.DropTable(
                name: "leave_records");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "payrolls");

            migrationBuilder.DropTable(
                name: "poll_vote_answers");

            migrationBuilder.DropTable(
                name: "role_privileges");

            migrationBuilder.DropTable(
                name: "school_leave_types");

            migrationBuilder.DropTable(
                name: "student_passed_exams");

            migrationBuilder.DropTable(
                name: "student_siblings");

            migrationBuilder.DropTable(
                name: "teacher_attendance");

            migrationBuilder.DropTable(
                name: "time_slots");

            migrationBuilder.DropTable(
                name: "timetable_entries");

            migrationBuilder.DropTable(
                name: "timetable_variations");

            migrationBuilder.DropTable(
                name: "user_parent_of");

            migrationBuilder.DropTable(
                name: "user_refresh_tokens");

            migrationBuilder.DropTable(
                name: "exams");

            migrationBuilder.DropTable(
                name: "leave_types");

            migrationBuilder.DropTable(
                name: "fee_invoices");

            migrationBuilder.DropTable(
                name: "payroll_runs");

            migrationBuilder.DropTable(
                name: "salary_structures");

            migrationBuilder.DropTable(
                name: "poll_options");

            migrationBuilder.DropTable(
                name: "poll_votes");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropTable(
                name: "timetables");

            migrationBuilder.DropTable(
                name: "grading_scales");

            migrationBuilder.DropTable(
                name: "leaves");

            migrationBuilder.DropTable(
                name: "fee_structures");

            migrationBuilder.DropTable(
                name: "poll_questions");

            migrationBuilder.DropTable(
                name: "polls");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "students");

            migrationBuilder.DropTable(
                name: "schools");
        }
    }
}
