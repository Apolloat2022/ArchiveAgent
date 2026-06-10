-- ============================================================
-- LEGACY stored procedure (the "before" state for the migration).
-- Converted to set-based Entity Framework logic in RecordArchiver.cs.
-- See MIGRATION.md for the EF equivalent and rationale.
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.sp_ArchiveOldRecords
    @CutoffUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRAN;

        INSERT INTO ArchivedRecords
            (OriginalRecordId, ExternalId, [Type], Title, Content, CreatedUtc, Category, RetentionClass, ArchivedUtc, ArchiveReason)
        SELECT
            Id, ExternalId, [Type], Title, Content, CreatedUtc, Category, RetentionClass, SYSUTCDATETIME(), 'legacy proc: past cutoff'
        FROM Records
        WHERE CreatedUtc < @CutoffUtc
          AND [Status] <> 'Archived'
          AND Category  <> 'PII';

        UPDATE Records
        SET [Status] = 'Archived'
        WHERE CreatedUtc < @CutoffUtc
          AND [Status] <> 'Archived'
          AND Category  <> 'PII';

    COMMIT;
END
