using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public static class NotificationSchema
{
    public static async Task Run(AppDb db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID('Notifications', 'U') IS NULL
            BEGIN
                CREATE TABLE Notifications (
                    Id int IDENTITY PRIMARY KEY,
                    UserId int NOT NULL,
                    EventKey nvarchar(100) NOT NULL,
                    Audience nvarchar(20) NOT NULL,
                    Kind nvarchar(20) NOT NULL,
                    EntityId int NOT NULL,
                    Title nvarchar(160) NOT NULL,
                    Message nvarchar(300) NOT NULL,
                    CreatedAtUtc datetime2 NOT NULL,
                    ReadAtUtc datetime2 NULL,
                    CONSTRAINT FK_Notifications_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(Id)
                );
                CREATE UNIQUE INDEX IX_Notifications_UserId_EventKey ON Notifications(UserId,EventKey);
                CREATE INDEX IX_Notifications_UserId_ReadAtUtc_Id ON Notifications(UserId,ReadAtUtc,Id);
            END;
            """
        );
    }
}
