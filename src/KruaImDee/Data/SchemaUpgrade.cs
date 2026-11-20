using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public static class SchemaUpgrade
{
    public static async Task Run(AppDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DECLARE @result int; EXEC @result = sp_getapplock "
                + "@Resource='KruaImDee-schema', @LockMode='Exclusive', @LockOwner='Transaction'; "
                + "IF @result < 0 THROW 51001, 'Schema upgrade busy', 1;"
        );
        // Zero VAT preserves every legacy bill and payment amount.
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Orders', 'PosRequestKey') IS NULL ALTER TABLE Orders ADD PosRequestKey nvarchar(64) NULL; "
                + "IF COL_LENGTH('Orders', 'PosOperatorId') IS NULL ALTER TABLE Orders ADD PosOperatorId int NULL; "
                + "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Orders_PosRequestKey' AND object_id=OBJECT_ID('Orders')) "
                + "EXEC('CREATE UNIQUE INDEX IX_Orders_PosRequestKey ON Orders(PosRequestKey) WHERE PosRequestKey IS NOT NULL');"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Orders', 'VatRate') IS NULL "
                + "ALTER TABLE Orders ADD VatRate decimal(12,2) NOT NULL CONSTRAINT DF_Orders_VatRate DEFAULT 0; "
                + "IF COL_LENGTH('Orders', 'VatAmount') IS NULL "
                + "ALTER TABLE Orders ADD VatAmount decimal(12,2) NOT NULL CONSTRAINT DF_Orders_VatAmount DEFAULT 0;"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Orders', 'PaymentExpiresAt') IS NULL ALTER TABLE Orders ADD PaymentExpiresAt datetime2 NULL; "
                + "IF COL_LENGTH('Payments', 'IsMock') IS NULL ALTER TABLE Payments ADD IsMock bit NOT NULL CONSTRAINT DF_Payments_IsMock DEFAULT 0; "
                + "IF COL_LENGTH('Payments', 'SlipFileName') IS NULL ALTER TABLE Payments ADD SlipFileName nvarchar(max) NOT NULL CONSTRAINT DF_Payments_SlipFileName DEFAULT N'';"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('OrderItems', 'OriginalUnitPrice') IS NULL BEGIN "
                + "ALTER TABLE OrderItems ADD OriginalUnitPrice decimal(12,2) NOT NULL CONSTRAINT DF_OrderItems_OriginalUnitPrice DEFAULT 0; "
                + "EXEC('UPDATE OrderItems SET OriginalUnitPrice = UnitPrice'); END; "
                + "IF COL_LENGTH('OrderItems', 'DiscountPercent') IS NULL "
                + "ALTER TABLE OrderItems ADD DiscountPercent decimal(12,2) NOT NULL CONSTRAINT DF_OrderItems_DiscountPercent DEFAULT 0;"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('MenuItems', 'DiscountPercent') IS NULL BEGIN "
                + "ALTER TABLE MenuItems ADD DiscountPercent decimal(12,2) NOT NULL CONSTRAINT DF_MenuItems_DiscountPercent DEFAULT 0; "
                + "EXEC(N'UPDATE MenuItems SET DiscountPercent = 10 WHERE Name=N''ข้าวผัดสูตรเอาอีก'' AND ImageUrl=''/images/food-01.jpg''; "
                + "UPDATE MenuItems SET DiscountPercent = 15 WHERE Name=N''สลัดโบวล์'' AND ImageUrl=''/images/food-07.jpg''; "
                + "UPDATE MenuItems SET DiscountPercent = 10 WHERE Name=N''พาสต้าเส้นสด'' AND ImageUrl=''/images/food-11.jpg'';'); "
                + "IF NOT EXISTS(SELECT 1 FROM Categories WHERE Name=N'ข้าว') INSERT Categories(Name,Active) VALUES(N'ข้าว',1); "
                + "IF NOT EXISTS(SELECT 1 FROM Categories WHERE Name=N'สเต๊ก') INSERT Categories(Name,Active) VALUES(N'สเต๊ก',1); "
                + "IF NOT EXISTS(SELECT 1 FROM Categories WHERE Name=N'พิซซ่าและเบอร์เกอร์') INSERT Categories(Name,Active) VALUES(N'พิซซ่าและเบอร์เกอร์',1); "
                + "IF NOT EXISTS(SELECT 1 FROM Categories WHERE Name=N'กาแฟ') INSERT Categories(Name,Active) VALUES(N'กาแฟ',1); "
                + "UPDATE MenuItems SET CategoryId=(SELECT TOP 1 Id FROM Categories WHERE Name=N'ข้าว' ORDER BY Id) WHERE Name=N'ข้าวผัดสูตรเอาอีก' AND ImageUrl='/images/food-01.jpg'; "
                + "UPDATE MenuItems SET CategoryId=(SELECT TOP 1 Id FROM Categories WHERE Name=N'สเต๊ก' ORDER BY Id) WHERE Name=N'สเต๊กย่างหอมสมุนไพร' AND ImageUrl='/images/food-04.jpg'; "
                + "UPDATE MenuItems SET CategoryId=(SELECT TOP 1 Id FROM Categories WHERE Name=N'พิซซ่าและเบอร์เกอร์' ORDER BY Id) WHERE (Name=N'พิซซ่าโฮมเมด' AND ImageUrl='/images/food-05.jpg') OR (Name=N'เบอร์เกอร์เนื้อย่าง' AND ImageUrl='/images/food-10.jpg'); "
                + "UPDATE MenuItems SET CategoryId=(SELECT TOP 1 Id FROM Categories WHERE Name=N'กาแฟ' ORDER BY Id) WHERE Name=N'กาแฟคั่วหอม' AND ImageUrl='/images/food-12.jpg'; "
                + "UPDATE Categories SET Active=0 WHERE Name=N'จานหลัก' AND NOT EXISTS(SELECT 1 FROM MenuItems WHERE CategoryId=Categories.Id AND Active=1); END;"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('Reviews', 'U') IS NULL BEGIN CREATE TABLE Reviews ("
                + "Id int IDENTITY PRIMARY KEY, UserId int NOT NULL, OrderId int NOT NULL, MenuItemId int NOT NULL, "
                + "Rating int NOT NULL, Comment nvarchar(1000) NOT NULL, CreatedAt datetime2 NOT NULL, "
                + "CONSTRAINT FK_Reviews_Users_UserId FOREIGN KEY(UserId) REFERENCES Users(Id), "
                + "CONSTRAINT FK_Reviews_Orders_OrderId FOREIGN KEY(OrderId) REFERENCES Orders(Id), "
                + "CONSTRAINT FK_Reviews_MenuItems_MenuItemId FOREIGN KEY(MenuItemId) REFERENCES MenuItems(Id), "
                + "CONSTRAINT CK_Reviews_Rating CHECK(Rating >= 1 AND Rating <= 5)); "
                + "CREATE UNIQUE INDEX IX_Reviews_OrderId_MenuItemId ON Reviews(OrderId,MenuItemId); END;"
        );
        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('Settings', 'TaxLegalName') IS NULL
                ALTER TABLE Settings ADD TaxLegalName nvarchar(max) NOT NULL DEFAULT N'ร้านเอาอีก (ONE MORE BITE)';
            IF COL_LENGTH('Settings', 'TaxAddress') IS NULL
                ALTER TABLE Settings ADD TaxAddress nvarchar(max) NOT NULL DEFAULT N'99 ถนนพหลโยธิน แขวงเสนานิคม เขตจตุจักร กรุงเทพมหานคร 10900';
            IF COL_LENGTH('Settings', 'TaxId') IS NULL
                ALTER TABLE Settings ADD TaxId nvarchar(max) NOT NULL DEFAULT N'0000000000000';
            IF COL_LENGTH('Settings', 'TaxBranch') IS NULL
                ALTER TABLE Settings ADD TaxBranch nvarchar(max) NOT NULL DEFAULT N'00000';
            IF OBJECT_ID('TaxInvoices', 'U') IS NULL BEGIN
                CREATE TABLE TaxInvoices (
                    Id int IDENTITY PRIMARY KEY, OrderId int NOT NULL, Number nvarchar(30) NOT NULL,
                    IssuedAt datetime2 NOT NULL, IssuedBy int NOT NULL,
                    SellerName nvarchar(max) NOT NULL, SellerAddress nvarchar(max) NOT NULL,
                    SellerTaxId nvarchar(max) NOT NULL, SellerBranch nvarchar(max) NOT NULL,
                    BuyerName nvarchar(max) NOT NULL, BuyerAddress nvarchar(max) NOT NULL,
                    BuyerTaxId nvarchar(max) NOT NULL, BuyerBranch nvarchar(max) NOT NULL,
                    LinesJson nvarchar(max) NOT NULL,
                    Subtotal decimal(12,2) NOT NULL, Discount decimal(12,2) NOT NULL,
                    VatRate decimal(12,2) NOT NULL, VatAmount decimal(12,2) NOT NULL, Total decimal(12,2) NOT NULL,
                    CONSTRAINT FK_TaxInvoices_Orders_OrderId FOREIGN KEY(OrderId) REFERENCES Orders(Id));
                CREATE UNIQUE INDEX IX_TaxInvoices_OrderId ON TaxInvoices(OrderId);
                CREATE UNIQUE INDEX IX_TaxInvoices_Number ON TaxInvoices(Number);
            END;
            """
        );
        await FinanceSchema.Run(db);
        await NotificationSchema.Run(db);
        await transaction.CommitAsync();
    }
}
