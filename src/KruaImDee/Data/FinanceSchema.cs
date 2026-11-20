using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public static class FinanceSchema
{
    public static async Task Run(AppDb db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID('RefundRequests','U') IS NULL BEGIN
                CREATE TABLE RefundRequests (
                    Id int IDENTITY PRIMARY KEY, PaymentId int NOT NULL REFERENCES Payments(Id),
                    ActivePaymentId int NULL, Amount decimal(12,2) NOT NULL,
                    Status nvarchar(24) NOT NULL, Reason nvarchar(500) NOT NULL,
                    RequestedBy int NOT NULL REFERENCES Users(Id), RequestedAtUtc datetime2 NOT NULL,
                    DecidedBy int NULL REFERENCES Users(Id), DecidedAtUtc datetime2 NULL,
                    DecisionReason nvarchar(500) NOT NULL,
                    Method nvarchar(20) NOT NULL, RecipientName nvarchar(100) NOT NULL, Destination nvarchar(300) NOT NULL,
                    ClaimedBy int NULL REFERENCES Users(Id), ClaimedAtUtc datetime2 NULL,
                    CompletedBy int NULL REFERENCES Users(Id), CompletedAtUtc datetime2 NULL, RefundedAtUtc datetime2 NULL,
                    Reference nvarchar(100) NOT NULL, EvidenceFileName nvarchar(100) NOT NULL,
                    RequestKey nvarchar(64) NOT NULL, CompletionKey nvarchar(64) NOT NULL, RowVersion rowversion NOT NULL,
                    CONSTRAINT CK_Refunds_Amount CHECK(Amount > 0),
                    CONSTRAINT CK_Refunds_Active CHECK(
                        (Status IN ('PendingApproval','Approved','Paying','Completed') AND ActivePaymentId IS NOT NULL AND ActivePaymentId=PaymentId)
                        OR (Status IN ('Rejected','Withdrawn','Revoked') AND ActivePaymentId IS NULL)));
                CREATE UNIQUE INDEX IX_RefundRequests_ActivePaymentId ON RefundRequests(ActivePaymentId) WHERE ActivePaymentId IS NOT NULL;
                CREATE UNIQUE INDEX IX_RefundRequests_RequestedBy_RequestKey ON RefundRequests(RequestedBy,RequestKey);
                CREATE INDEX IX_RefundRequests_PaymentId ON RefundRequests(PaymentId);
                CREATE INDEX IX_RefundRequests_RefundedAtUtc ON RefundRequests(RefundedAtUtc);
                CREATE INDEX IX_RefundRequests_DecidedBy ON RefundRequests(DecidedBy);
                CREATE INDEX IX_RefundRequests_ClaimedBy ON RefundRequests(ClaimedBy);
                CREATE INDEX IX_RefundRequests_CompletedBy ON RefundRequests(CompletedBy);
            END;
            IF OBJECT_ID('Reconciliations','U') IS NULL BEGIN
                CREATE TABLE Reconciliations (
                    Id int IDENTITY PRIMARY KEY, BusinessDate datetime2 NOT NULL,
                    ExpectedCash decimal(12,2) NOT NULL, ExpectedTransfer decimal(12,2) NOT NULL,
                    CountedCash decimal(12,2) NOT NULL, CountedTransfer decimal(12,2) NOT NULL,
                    Note nvarchar(500) NOT NULL, RecordedBy int NOT NULL REFERENCES Users(Id), RecordedAtUtc datetime2 NOT NULL);
                CREATE INDEX IX_Reconciliations_BusinessDate ON Reconciliations(BusinessDate);
                CREATE INDEX IX_Reconciliations_RecordedBy ON Reconciliations(RecordedBy);
            END;
            """
        );
    }
}
