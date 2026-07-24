using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

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
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    school_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    username = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    meta = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fee_structures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "INR"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_username = table.Column<string>(type: "text", nullable: true),
                    last_edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_edited_by_username = table.Column<string>(type: "text", nullable: true),
                    edit_history = table.Column<string>(type: "jsonb", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_structures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grading_scales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    passing_mark = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 33m),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grading_scales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: true),
                    total_teachers = table.Column<int>(type: "integer", nullable: false),
                    payrolls_generated = table.Column<int>(type: "integer", nullable: false),
                    payrolls_skipped = table.Column<int>(type: "integer", nullable: false),
                    payrolls_locked = table.Column<int>(type: "integer", nullable: false),
                    payrolls_paid = table.Column<int>(type: "integer", nullable: false),
                    payrolls_failed = table.Column<int>(type: "integer", nullable: false),
                    total_gross = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    skipped = table.Column<string>(type: "jsonb", nullable: false),
                    razorpay_batch_id = table.Column<string>(type: "text", nullable: true),
                    batch_transfer_id = table.Column<string>(type: "text", nullable: true),
                    batch_status = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    transferred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_runs", x => x.id);
                    table.CheckConstraint("ck_payroll_runs_period", "month BETWEEN 1 AND 12 AND year BETWEEN 2000 AND 2100");
                });

            migrationBuilder.CreateTable(
                name: "role_privileges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    privilege = table.Column<string>(type: "text", nullable: false),
                    roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_privileges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_hindi = table.Column<string>(type: "text", nullable: true),
                    slug = table.Column<string>(type: "citext", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    pincode = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "citext", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    primary_color = table.Column<string>(type: "text", nullable: false, defaultValue: "#1e40af"),
                    academic_year = table.Column<string>(type: "text", nullable: false, defaultValue: "2025-2026"),
                    academic_year_start_month = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    classes = table.Column<List<string>>(type: "text[]", nullable: false),
                    sections = table.Column<List<string>>(type: "text[]", nullable: false),
                    working_days = table.Column<List<string>>(type: "text[]", nullable: false),
                    leave_require_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    fee_billing_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    fee_billing_day = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    fee_reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    fee_reminder_day = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    plan = table.Column<int>(type: "integer", nullable: false),
                    plan_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    razorpay_key_id = table.Column<string>(type: "text", nullable: true),
                    razorpay_key_secret = table.Column<string>(type: "text", nullable: true),
                    payment_vpa = table.Column<string>(type: "text", nullable: true),
                    payment_payee_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schools", x => x.id);
                    table.CheckConstraint("ck_schools_ay_start_month", "academic_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_schools_fee_days", "fee_billing_day BETWEEN 1 AND 28 AND fee_reminder_day BETWEEN 1 AND 28");
                    table.CheckConstraint("ck_schools_working_days", "working_days <@ ARRAY['Mon','Tue','Wed','Thu','Fri','Sat','Sun']::text[]");
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: true),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    is_co_scholastic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    default_max_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: false, defaultValue: 100m),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    slot_number = table.Column<int>(type: "integer", nullable: false),
                    default_start_time = table.Column<string>(type: "text", nullable: false),
                    default_end_time = table.Column<string>(type: "text", nullable: false),
                    day_times = table.Column<string>(type: "jsonb", nullable: false),
                    spans_multiple_periods = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    next_slot_number = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_slots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "timetables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    term = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetables", x => x.id);
                    table.CheckConstraint("ck_timetables_dates", "to_date IS NULL OR to_date >= from_date");
                });

            migrationBuilder.CreateTable(
                name: "fee_heads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fee_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_heads", x => x.id);
                    table.CheckConstraint("ck_fee_heads_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_fee_heads_fee_structures_fee_structure_id",
                        column: x => x.fee_structure_id,
                        principalTable: "fee_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fee_installments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fee_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_installments", x => x.id);
                    table.CheckConstraint("ck_fee_installments_pct", "percentage > 0 AND percentage <= 100");
                    table.ForeignKey(
                        name: "fk_fee_installments_fee_structures_fee_structure_id",
                        column: x => x.fee_structure_id,
                        principalTable: "fee_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: true),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    grading_scale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight_in_final = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.id);
                    table.CheckConstraint("ck_exams_dates", "to_date >= from_date");
                    table.CheckConstraint("ck_exams_weight", "weight_in_final BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_exams_grading_scales_grading_scale_id",
                        column: x => x.grading_scale_id,
                        principalTable: "grading_scales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grading_bands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    grading_scale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    max_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    grade = table.Column<string>(type: "text", nullable: false),
                    gpa = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_passing = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grading_bands", x => x.id);
                    table.CheckConstraint("ck_grading_bands_range", "min_percent BETWEEN 0 AND 100 AND max_percent BETWEEN 0 AND 100 AND max_percent >= min_percent AND (gpa IS NULL OR gpa BETWEEN 0 AND 10)");
                    table.ForeignKey(
                        name: "fk_grading_bands_grading_scales_grading_scale_id",
                        column: x => x.grading_scale_id,
                        principalTable: "grading_scales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "school_leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    total_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_leave_types", x => x.id);
                    table.CheckConstraint("ck_school_leave_types_days", "total_days >= 0");
                    table.ForeignKey(
                        name: "fk_school_leave_types_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    admission_no = table.Column<string>(type: "text", nullable: false),
                    roll_no = table.Column<string>(type: "text", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    first_name_hi = table.Column<string>(type: "text", nullable: true),
                    last_name_hi = table.Column<string>(type: "text", nullable: true),
                    dob = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<int>(type: "integer", nullable: true),
                    blood_group = table.Column<string>(type: "text", nullable: true),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    admission_date = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    address = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    pincode = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "citext", nullable: true),
                    father_name = table.Column<string>(type: "text", nullable: true),
                    father_name_hi = table.Column<string>(type: "text", nullable: true),
                    father_phone = table.Column<string>(type: "text", nullable: true),
                    father_occup = table.Column<string>(type: "text", nullable: true),
                    mother_name = table.Column<string>(type: "text", nullable: true),
                    mother_name_hi = table.Column<string>(type: "text", nullable: true),
                    mother_phone = table.Column<string>(type: "text", nullable: true),
                    mother_occup = table.Column<string>(type: "text", nullable: true),
                    guardian_name = table.Column<string>(type: "text", nullable: true),
                    guardian_phone = table.Column<string>(type: "text", nullable: true),
                    guardian_rel = table.Column<string>(type: "text", nullable: true),
                    aadhar_no = table.Column<string>(type: "text", nullable: true),
                    aadhar_doc = table.Column<string>(type: "text", nullable: true),
                    aadhar_doc_key = table.Column<string>(type: "text", nullable: true),
                    birth_cert_no = table.Column<string>(type: "text", nullable: true),
                    birth_doc = table.Column<string>(type: "text", nullable: true),
                    birth_doc_key = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<int>(type: "integer", nullable: true),
                    caste = table.Column<string>(type: "text", nullable: true),
                    religion = table.Column<int>(type: "integer", nullable: true),
                    mother_tongue = table.Column<string>(type: "text", nullable: true),
                    nationality = table.Column<string>(type: "text", nullable: false, defaultValue: "Indian"),
                    transport_mode = table.Column<int>(type: "integer", nullable: true),
                    bus_route = table.Column<string>(type: "text", nullable: true),
                    pickup_point = table.Column<string>(type: "text", nullable: true),
                    prev_school = table.Column<string>(type: "text", nullable: true),
                    prev_class = table.Column<string>(type: "text", nullable: true),
                    tc_no = table.Column<string>(type: "text", nullable: true),
                    tc_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tc_doc = table.Column<string>(type: "text", nullable: true),
                    tc_doc_key = table.Column<string>(type: "text", nullable: true),
                    house = table.Column<string>(type: "text", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    share_token = table.Column<string>(type: "text", nullable: true),
                    share_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_students", x => x.id);
                    table.ForeignKey(
                        name: "fk_students_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_name = table.Column<string>(type: "text", nullable: false),
                    max_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    passing_mark = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    theory_max = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    practical_max = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    exam_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_time = table.Column<string>(type: "text", nullable: true),
                    duration_mins = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_subjects", x => x.id);
                    table.CheckConstraint("ck_exam_subjects_marks", "max_marks >= 0 AND (theory_max IS NULL OR theory_max >= 0) AND (practical_max IS NULL OR practical_max >= 0)");
                    table.ForeignKey(
                        name: "fk_exam_subjects_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_subjects_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_structure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_no = table.Column<string>(type: "text", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    student_name = table.Column<string>(type: "text", nullable: true),
                    student_adm_no = table.Column<string>(type: "text", nullable: true),
                    student_class = table.Column<string>(type: "text", nullable: true),
                    student_section = table.Column<string>(type: "text", nullable: true),
                    installment_name = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    discount = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    discount_reason = table.Column<string>(type: "text", nullable: true),
                    late_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    balance = table.Column<decimal>(type: "numeric(12,2)", nullable: false, computedColumnSql: "GREATEST(0::numeric, total - amount_paid)", stored: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_invoices", x => x.id);
                    table.CheckConstraint("ck_fee_invoices_amounts", "subtotal >= 0 AND discount >= 0 AND late_fee >= 0 AND total >= 0 AND amount_paid >= 0");
                    table.CheckConstraint("ck_fee_invoices_no_stored_overdue", "status <> 3");
                    table.ForeignKey(
                        name: "fk_fee_invoices_fee_structures_fee_structure_id",
                        column: x => x.fee_structure_id,
                        principalTable: "fee_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fee_invoices_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_passed_exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_name = table.Column<string>(type: "text", nullable: true),
                    institution = table.Column<string>(type: "text", nullable: true),
                    year = table.Column<string>(type: "text", nullable: true),
                    roll_no = table.Column<string>(type: "text", nullable: true),
                    board = table.Column<string>(type: "text", nullable: true),
                    max_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    obtained_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_passed_exams", x => x.id);
                    table.CheckConstraint("ck_passed_exams_marks", "(max_marks IS NULL OR max_marks >= 0) AND (obtained_marks IS NULL OR obtained_marks >= 0)");
                    table.ForeignKey(
                        name: "fk_student_passed_exams_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_siblings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    @class = table.Column<string>(name: "class", type: "text", nullable: true),
                    relation = table.Column<int>(type: "integer", nullable: true),
                    same_school = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_siblings", x => x.id);
                    table.ForeignKey(
                        name: "fk_student_siblings_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    school_id = table.Column<Guid>(type: "uuid", nullable: true),
                    school_slug = table.Column<string>(type: "text", nullable: true),
                    username = table.Column<string>(type: "citext", nullable: false),
                    password = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    password_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_schools_school_id",
                        column: x => x.school_id,
                        principalTable: "schools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_name = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_fee_invoice_lines_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_fee_invoice_lines_fee_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "fee_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<short>(type: "smallint", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "text", nullable: true),
                    arrived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    leave_reason = table.Column<string>(type: "text", nullable: true),
                    marked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    marked_by_name = table.Column<string>(type: "text", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance", x => x.id);
                    table.CheckConstraint("ck_attendance_period_mode", "(mode = 'period') = (period IS NOT NULL) AND (period IS NULL OR period BETWEEN 1 AND 12)");
                    table.ForeignKey(
                        name: "fk_attendance_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_users_marked_by_user_id",
                        column: x => x.marked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "class_teachers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_teachers", x => x.id);
                    table.ForeignKey(
                        name: "fk_class_teachers_users_teacher_user_id",
                        column: x => x.teacher_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_name = table.Column<string>(type: "text", nullable: true),
                    subject_name = table.Column<string>(type: "text", nullable: true),
                    student_name = table.Column<string>(type: "text", nullable: true),
                    student_adm_no = table.Column<string>(type: "text", nullable: true),
                    @class = table.Column<string>(name: "class", type: "text", nullable: true),
                    section = table.Column<string>(type: "text", nullable: true),
                    academic_year = table.Column<string>(type: "text", nullable: true),
                    marks_obtained = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    max_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    theory_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    practical_marks = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    percentage = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    grade = table.Column<string>(type: "text", nullable: true),
                    gpa = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    is_passing = table.Column<bool>(type: "boolean", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "text", nullable: true),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_by_name = table.Column<string>(type: "text", nullable: true),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_results", x => x.id);
                    table.CheckConstraint("ck_exam_results_absent", "status <> 'absent' OR marks_obtained IS NULL");
                    table.CheckConstraint("ck_exam_results_ranges", "(marks_obtained IS NULL OR marks_obtained >= 0) AND max_marks >= 0 AND (percentage IS NULL OR percentage BETWEEN 0 AND 100) AND (gpa IS NULL OR gpa BETWEEN 0 AND 10)");
                    table.ForeignKey(
                        name: "fk_exam_results_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_results_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exam_results_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exam_results_users_entered_by_user_id",
                        column: x => x.entered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leaves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaves", x => x.id);
                    table.ForeignKey(
                        name: "fk_leaves_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_no = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    razorpay_order_id = table.Column<string>(type: "text", nullable: true),
                    razorpay_payment_id = table.Column<string>(type: "text", nullable: true),
                    razorpay_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cheque_no = table.Column<string>(type: "text", nullable: true),
                    cheque_bank = table.Column<string>(type: "text", nullable: true),
                    cheque_date = table.Column<DateOnly>(type: "date", nullable: true),
                    transaction_ref = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    collected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_payments_fee_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "fee_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_collected_by_user_id",
                        column: x => x.collected_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "polls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false),
                    target_roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    show_results_before_close = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    allow_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polls", x => x.id);
                    table.CheckConstraint("ck_polls_target_roles", "target_roles <@ ARRAY['parent','teacher','student','school_admin','principal','accountant']::text[]");
                    table.ForeignKey(
                        name: "fk_polls_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "salary_structures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    base_salary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    da = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    hra = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    ta = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    other_allowances = table.Column<string>(type: "jsonb", nullable: false),
                    pf = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    esi = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    professional_tax = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    income_tax = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    other_deductions = table.Column<string>(type: "jsonb", nullable: false),
                    leave_deduction_per_day = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    bank_account_number = table.Column<string>(type: "text", nullable: true),
                    bank_ifsc = table.Column<string>(type: "text", nullable: true),
                    bank_account_holder = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_structures", x => x.id);
                    table.CheckConstraint("ck_salary_structures_pcts", "base_salary >= 0 AND da BETWEEN 0 AND 200 AND hra BETWEEN 0 AND 200 AND ta >= 0 AND pf BETWEEN 0 AND 100 AND esi BETWEEN 0 AND 100 AND professional_tax >= 0 AND income_tax BETWEEN 0 AND 100 AND leave_deduction_per_day >= 0");
                    table.ForeignKey(
                        name: "fk_salary_structures_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    check_in = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    check_out = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    on_duty_note = table.Column<string>(type: "text", nullable: true),
                    remarks = table.Column<string>(type: "text", nullable: true),
                    teacher_name = table.Column<string>(type: "text", nullable: true),
                    marked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    marked_by_name = table.Column<string>(type: "text", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_attendance", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_attendance_users_marked_by_user_id",
                        column: x => x.marked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_teacher_attendance_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timetable_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    timetable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    slot_number = table.Column<int>(type: "integer", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_name = table.Column<string>(type: "text", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_name = table.Column<string>(type: "text", nullable: true),
                    room = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetable_entries", x => x.id);
                    table.CheckConstraint("ck_timetable_entries_dow", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_timetable_entries_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_timetable_entries_timetables_timetable_id",
                        column: x => x.timetable_id,
                        principalTable: "timetables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_timetable_entries_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "timetable_variations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    timetable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    slot_number = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_name = table.Column<string>(type: "text", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_name = table.Column<string>(type: "text", nullable: true),
                    room = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetable_variations", x => x.id);
                    table.CheckConstraint("ck_timetable_variations_dow", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_timetable_variations_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_timetable_variations_timetables_timetable_id",
                        column: x => x.timetable_id,
                        principalTable: "timetables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_timetable_variations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_timetable_variations_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
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
                    table.PrimaryKey("pk_user_parent_of", x => new { x.user_id, x.student_id });
                    table.ForeignKey(
                        name: "fk_user_parent_of_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_parent_of_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    leave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    total_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    used_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false, defaultValue: 0m),
                    balance_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false, computedColumnSql: "total_days - used_days", stored: true),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_types_leaves_leave_id",
                        column: x => x.leave_id,
                        principalTable: "leaves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_questions_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    user_role = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_votes", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_votes_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_poll_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payrolls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    salary_structure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    month = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    academic_year = table.Column<string>(type: "text", nullable: true),
                    teacher_name = table.Column<string>(type: "text", nullable: true),
                    teacher_email = table.Column<string>(type: "text", nullable: true),
                    teacher_username = table.Column<string>(type: "text", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    bank_ifsc = table.Column<string>(type: "text", nullable: true),
                    bank_account_holder = table.Column<string>(type: "text", nullable: true),
                    base_salary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    da = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    hra = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ta = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    other_allowances = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    other_allowance_items = table.Column<string>(type: "jsonb", nullable: false),
                    gross_salary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    pf = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    esi = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    professional_tax = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    income_tax = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    leave_deduction = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    other_deduction_items = table.Column<string>(type: "jsonb", nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    net_salary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    unpaid_leave_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    daily_rate = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    razorpay_transfer_id = table.Column<string>(type: "text", nullable: true),
                    transferred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    transfer_status = table.Column<string>(type: "text", nullable: true),
                    transfer_failure_reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payrolls", x => x.id);
                    table.CheckConstraint("ck_payrolls_nonneg", "base_salary >= 0 AND gross_salary >= 0 AND total_deductions >= 0 AND net_salary >= 0 AND unpaid_leave_days >= 0 AND daily_rate >= 0");
                    table.CheckConstraint("ck_payrolls_period", "month BETWEEN 1 AND 12 AND year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "fk_payrolls_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalTable: "payroll_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_payrolls_salary_structures_salary_structure_id",
                        column: x => x.salary_structure_id,
                        principalTable: "salary_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_payrolls_users_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    leave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_records", x => x.id);
                    table.CheckConstraint("ck_leave_records_dates", "to_date >= from_date");
                    table.ForeignKey(
                        name: "fk_leave_records_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leave_records_leaves_leave_id",
                        column: x => x.leave_id,
                        principalTable: "leaves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_options_poll_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "poll_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_vote_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    vote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_vote_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_vote_answers_poll_options_option_id",
                        column: x => x.option_id,
                        principalTable: "poll_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_poll_vote_answers_poll_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "poll_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_poll_vote_answers_poll_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "poll_votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marked_by_user_id",
                table: "attendance",
                column: "marked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_roster",
                table: "attendance",
                columns: new[] { "school_id", "class", "section", "date", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_student_history",
                table: "attendance",
                columns: new[] { "school_id", "student_id", "date" },
                unique: true,
                descending: new[] { false, false, true },
                filter: "mode = 'daily'");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_student_id",
                table: "attendance",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "uq_attendance_period",
                table: "attendance",
                columns: new[] { "school_id", "student_id", "date", "period", "subject" },
                unique: true,
                filter: "mode = 'period'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_meta",
                table: "audit_logs",
                column: "meta")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_school_time",
                table: "audit_logs",
                columns: new[] { "school_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_class_teachers_by_teacher",
                table: "class_teachers",
                columns: new[] { "school_id", "teacher_user_id", "academic_year" });

            migrationBuilder.CreateIndex(
                name: "ix_class_teachers_teacher_user_id",
                table: "class_teachers",
                column: "teacher_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_class_teachers_assignment",
                table: "class_teachers",
                columns: new[] { "school_id", "academic_year", "class", "section", "subject", "teacher_user_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_entered_by_user_id",
                table: "exam_results",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_exam_class",
                table: "exam_results",
                columns: new[] { "school_id", "exam_id", "class", "section" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_exam_id",
                table: "exam_results",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_student_id",
                table: "exam_results",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_student_year",
                table: "exam_results",
                columns: new[] { "school_id", "student_id", "academic_year" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_subject_id",
                table: "exam_results",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uq_exam_results_key",
                table: "exam_results",
                columns: new[] { "school_id", "exam_id", "student_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_subjects_subject_id",
                table: "exam_subjects",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uq_exam_subjects_exam_subject",
                table: "exam_subjects",
                columns: new[] { "exam_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exams_grading_scale_id",
                table: "exams",
                column: "grading_scale_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_lookup",
                table: "exams",
                columns: new[] { "school_id", "academic_year", "class", "section", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_heads_fee_structure_id",
                table: "fee_heads",
                column: "fee_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_installments_fee_structure_id",
                table: "fee_installments",
                column: "fee_structure_id");

            migrationBuilder.CreateIndex(
                name: "uq_fee_installments_name",
                table: "fee_installments",
                columns: new[] { "fee_structure_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoice_lines_head",
                table: "fee_invoice_lines",
                column: "head_name");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoice_lines_invoice_id",
                table: "fee_invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_due_status",
                table: "fee_invoices",
                columns: new[] { "school_id", "due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_fee_structure_id",
                table: "fee_invoices",
                column: "fee_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_student_id",
                table: "fee_invoices",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_invoices_student_year",
                table: "fee_invoices",
                columns: new[] { "school_id", "student_id", "academic_year" });

            migrationBuilder.CreateIndex(
                name: "uq_fee_invoices_school_no",
                table: "fee_invoices",
                columns: new[] { "school_id", "invoice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_structures_lookup",
                table: "fee_structures",
                columns: new[] { "school_id", "academic_year", "class", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_grading_bands_grading_scale_id",
                table: "grading_bands",
                column: "grading_scale_id");

            migrationBuilder.CreateIndex(
                name: "uq_grading_scales_one_default",
                table: "grading_scales",
                column: "school_id",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "uq_grading_scales_school_name",
                table: "grading_scales",
                columns: new[] { "school_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_records_leave_id",
                table: "leave_records",
                column: "leave_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_records_leave_type_id",
                table: "leave_records",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_leave_types_name",
                table: "leave_types",
                columns: new[] { "leave_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leaves_teacher_id",
                table: "leaves",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "uq_leaves_teacher_year",
                table: "leaves",
                columns: new[] { "school_id", "teacher_id", "academic_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_collected_by_user_id",
                table: "payments",
                column: "collected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_id",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid",
                table: "payments",
                columns: new[] { "school_id", "paid_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_rzp_order",
                table: "payments",
                column: "razorpay_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_student_id",
                table: "payments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "uq_payments_idem",
                table: "payments",
                columns: new[] { "school_id", "invoice_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_receipt",
                table: "payments",
                columns: new[] { "school_id", "receipt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_payments_rzp",
                table: "payments",
                column: "razorpay_payment_id",
                unique: true,
                filter: "razorpay_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payroll_runs_period",
                table: "payroll_runs",
                columns: new[] { "school_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_payroll_run_id",
                table: "payrolls",
                column: "payroll_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_run",
                table: "payrolls",
                columns: new[] { "school_id", "payroll_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_salary_structure_id",
                table: "payrolls",
                column: "salary_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_status_period",
                table: "payrolls",
                columns: new[] { "school_id", "status", "year", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_payrolls_teacher_id",
                table: "payrolls",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "uq_payrolls_teacher_period",
                table: "payrolls",
                columns: new[] { "school_id", "teacher_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_poll_options_question_id",
                table: "poll_options",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_questions_poll_id",
                table: "poll_questions",
                column: "poll_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_vote_answers_option",
                table: "poll_vote_answers",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_vote_answers_question_id",
                table: "poll_vote_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "uq_poll_vote_answers_question",
                table: "poll_vote_answers",
                columns: new[] { "vote_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_poll_votes_school",
                table: "poll_votes",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_votes_user_id",
                table: "poll_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_poll_votes_one_per_user",
                table: "poll_votes",
                columns: new[] { "poll_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_polls_created_by_user_id",
                table: "polls",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_polls_school_status",
                table: "polls",
                columns: new[] { "school_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_role_privileges_roles",
                table: "role_privileges",
                column: "roles")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "uq_role_privileges_school_priv",
                table: "role_privileges",
                columns: new[] { "school_id", "privilege" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_structures_teacher_id",
                table: "salary_structures",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "uq_salary_structures_teacher_year",
                table: "salary_structures",
                columns: new[] { "school_id", "teacher_id", "academic_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_school_leave_types_school_id_name",
                table: "school_leave_types",
                columns: new[] { "school_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schools_slug",
                table: "schools",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_passed_exams_student_id",
                table: "student_passed_exams",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_siblings_student_id",
                table: "student_siblings",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_students_admno_trgm",
                table: "students",
                column: "admission_no")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_deleted",
                table: "students",
                column: "is_deleted",
                filter: "is_deleted = true");

            migrationBuilder.CreateIndex(
                name: "ix_students_firstname_trgm",
                table: "students",
                column: "first_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_lastname_trgm",
                table: "students",
                column: "last_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_students_school_class_section",
                table: "students",
                columns: new[] { "school_id", "class", "section" });

            migrationBuilder.CreateIndex(
                name: "ix_students_share_token",
                table: "students",
                column: "share_token",
                filter: "share_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_students_school_admission",
                table: "students",
                columns: new[] { "school_id", "admission_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_subjects_school_year_class_name",
                table: "subjects",
                columns: new[] { "school_id", "academic_year", "class", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_history",
                table: "teacher_attendance",
                columns: new[] { "school_id", "teacher_id", "date" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_marked_by_user_id",
                table: "teacher_attendance",
                column: "marked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_roster",
                table: "teacher_attendance",
                columns: new[] { "school_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_attendance_teacher_id",
                table: "teacher_attendance",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "uq_time_slots_school_number",
                table: "time_slots",
                columns: new[] { "school_id", "slot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_grid",
                table: "timetable_entries",
                columns: new[] { "timetable_id", "day_of_week", "slot_number" });

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_subject_id",
                table: "timetable_entries",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_teacher",
                table: "timetable_entries",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_created_by_user_id",
                table: "timetable_variations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_date",
                table: "timetable_variations",
                columns: new[] { "school_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_subject_id",
                table: "timetable_variations",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_teacher_id",
                table: "timetable_variations",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_timetable_variations_timetable_id",
                table: "timetable_variations",
                column: "timetable_id");

            migrationBuilder.CreateIndex(
                name: "uq_timetable_variations_key",
                table: "timetable_variations",
                columns: new[] { "school_id", "timetable_id", "date", "slot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timetables_lookup",
                table: "timetables",
                columns: new[] { "school_id", "class", "section", "academic_year", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_user_parent_of_student_id",
                table: "user_parent_of",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_expiry",
                table: "user_refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_hash",
                table: "user_refresh_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_user",
                table: "user_refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_student_id",
                table: "users",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "uq_users_global_username",
                table: "users",
                column: "username",
                unique: true,
                filter: "school_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_users_school_username",
                table: "users",
                columns: new[] { "school_id", "username" },
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
