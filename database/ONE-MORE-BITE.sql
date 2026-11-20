-- ONE MORE BITE: SQL Server schema and clean startup data.
-- Run on an empty database. Existing tables are never dropped or overwritten.
USE [master];
GO
IF DB_ID(N'KruaImDee') IS NULL EXEC(N'CREATE DATABASE [KruaImDee]');
GO
USE [KruaImDee];
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
    IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped=0)
        THROW 51000, 'Database is not empty. No changes were made.', 1;
    BEGIN TRANSACTION;

CREATE TABLE [AuditLogs] (
    [Id] int NOT NULL IDENTITY,
    [CreatedAt] datetime2 NOT NULL,
    [ActorId] int NOT NULL,
    [Action] nvarchar(max) NOT NULL,
    [Detail] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);


CREATE TABLE [Categories] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(80) NOT NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
);


CREATE TABLE [DiningTables] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(30) NOT NULL,
    [Capacity] int NOT NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_DiningTables] PRIMARY KEY ([Id])
);


CREATE TABLE [Promotions] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Percentage] bit NOT NULL,
    [Value] decimal(12,2) NOT NULL,
    [Minimum] decimal(12,2) NOT NULL,
    [StartsAt] datetime2 NOT NULL,
    [EndsAt] datetime2 NOT NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_Promotions] PRIMARY KEY ([Id])
);


CREATE TABLE [Settings] (
    [Id] int NOT NULL IDENTITY,
    [TaxLegalName] nvarchar(max) NOT NULL,
    [TaxAddress] nvarchar(max) NOT NULL,
    [TaxId] nvarchar(max) NOT NULL,
    [TaxBranch] nvarchar(max) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Phone] nvarchar(max) NOT NULL,
    [Address] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
);


CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [Email] nvarchar(180) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [Phone] nvarchar(max) NOT NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);


CREATE TABLE [MenuItems] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(120) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Price] decimal(12,2) NOT NULL,
    [DiscountPercent] decimal(12,2) NOT NULL,
    [CategoryId] int NOT NULL,
    [ImageUrl] nvarchar(max) NOT NULL,
    [Active] bit NOT NULL,
    [SoldOut] bit NOT NULL,
    CONSTRAINT [PK_MenuItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MenuItems_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Notifications] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [EventKey] nvarchar(100) NOT NULL,
    [Audience] nvarchar(20) NOT NULL,
    [Kind] nvarchar(20) NOT NULL,
    [EntityId] int NOT NULL,
    [Title] nvarchar(160) NOT NULL,
    [Message] nvarchar(300) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ReadAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [PosRequestKey] nvarchar(64) NULL,
    [PosOperatorId] int NULL,
    [UserId] int NOT NULL,
    [DiningTableId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [Type] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [Note] nvarchar(max) NOT NULL,
    [PromoCode] nvarchar(max) NOT NULL,
    [Subtotal] decimal(12,2) NOT NULL,
    [Discount] decimal(12,2) NOT NULL,
    [VatRate] decimal(12,2) NOT NULL,
    [VatAmount] decimal(12,2) NOT NULL,
    [PaymentExpiresAt] datetime2 NULL,
    [Total] decimal(12,2) NOT NULL,
    [Closed] bit NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Orders_DiningTables_DiningTableId] FOREIGN KEY ([DiningTableId]) REFERENCES [DiningTables] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Reconciliations] (
    [Id] int NOT NULL IDENTITY,
    [BusinessDate] datetime2 NOT NULL,
    [ExpectedCash] decimal(12,2) NOT NULL,
    [ExpectedTransfer] decimal(12,2) NOT NULL,
    [CountedCash] decimal(12,2) NOT NULL,
    [CountedTransfer] decimal(12,2) NOT NULL,
    [Note] nvarchar(500) NOT NULL,
    [RecordedBy] int NOT NULL,
    [RecordedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Reconciliations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Reconciliations_Users_RecordedBy] FOREIGN KEY ([RecordedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Reservations] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [DiningTableId] int NULL,
    [StartsAt] datetime2 NOT NULL,
    [EndsAt] datetime2 NOT NULL,
    [Guests] int NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Phone] nvarchar(max) NOT NULL,
    [Note] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Reservations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Reservations_DiningTables_DiningTableId] FOREIGN KEY ([DiningTableId]) REFERENCES [DiningTables] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Reservations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [OrderItems] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [MenuItemId] int NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [UnitPrice] decimal(12,2) NOT NULL,
    [OriginalUnitPrice] decimal(12,2) NOT NULL,
    [DiscountPercent] decimal(12,2) NOT NULL,
    [Quantity] int NOT NULL,
    [Note] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderItems_MenuItems_MenuItemId] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Payments] (
    [Id] int NOT NULL IDENTITY,
    [IsMock] bit NOT NULL,
    [SlipFileName] nvarchar(max) NOT NULL,
    [OrderId] int NOT NULL,
    [Amount] decimal(12,2) NOT NULL,
    [Received] decimal(12,2) NOT NULL,
    [Change] decimal(12,2) NOT NULL,
    [Method] nvarchar(max) NOT NULL,
    [PaidAt] datetime2 NOT NULL,
    [ActorId] int NOT NULL,
    [Refunded] bit NOT NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Payments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [Reviews] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [OrderId] int NOT NULL,
    [MenuItemId] int NOT NULL,
    [Rating] int NOT NULL,
    [Comment] nvarchar(1000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Reviews] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Reviews_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
    CONSTRAINT [FK_Reviews_MenuItems_MenuItemId] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Reviews_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Reviews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [TaxInvoices] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [Number] nvarchar(30) NOT NULL,
    [IssuedAt] datetime2 NOT NULL,
    [IssuedBy] int NOT NULL,
    [SellerName] nvarchar(max) NOT NULL,
    [SellerAddress] nvarchar(max) NOT NULL,
    [SellerTaxId] nvarchar(max) NOT NULL,
    [SellerBranch] nvarchar(max) NOT NULL,
    [BuyerName] nvarchar(max) NOT NULL,
    [BuyerAddress] nvarchar(max) NOT NULL,
    [BuyerTaxId] nvarchar(max) NOT NULL,
    [BuyerBranch] nvarchar(max) NOT NULL,
    [LinesJson] nvarchar(max) NOT NULL,
    [Subtotal] decimal(12,2) NOT NULL,
    [Discount] decimal(12,2) NOT NULL,
    [VatRate] decimal(12,2) NOT NULL,
    [VatAmount] decimal(12,2) NOT NULL,
    [Total] decimal(12,2) NOT NULL,
    CONSTRAINT [PK_TaxInvoices] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TaxInvoices_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [RefundRequests] (
    [Id] int NOT NULL IDENTITY,
    [PaymentId] int NOT NULL,
    [ActivePaymentId] int NULL,
    [Amount] decimal(12,2) NOT NULL,
    [Status] nvarchar(24) NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [RequestedBy] int NOT NULL,
    [RequestedAtUtc] datetime2 NOT NULL,
    [DecidedBy] int NULL,
    [DecidedAtUtc] datetime2 NULL,
    [DecisionReason] nvarchar(500) NOT NULL,
    [Method] nvarchar(20) NOT NULL,
    [RecipientName] nvarchar(100) NOT NULL,
    [Destination] nvarchar(300) NOT NULL,
    [ClaimedBy] int NULL,
    [ClaimedAtUtc] datetime2 NULL,
    [CompletedBy] int NULL,
    [CompletedAtUtc] datetime2 NULL,
    [RefundedAtUtc] datetime2 NULL,
    [Reference] nvarchar(100) NOT NULL,
    [EvidenceFileName] nvarchar(100) NOT NULL,
    [RequestKey] nvarchar(64) NOT NULL,
    [CompletionKey] nvarchar(64) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_RefundRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Refunds_Active] CHECK (([Status] IN ('PendingApproval','Approved','Paying','Completed') AND [ActivePaymentId] IS NOT NULL AND [ActivePaymentId]=[PaymentId]) OR ([Status] IN ('Rejected','Withdrawn','Revoked') AND [ActivePaymentId] IS NULL)),
    CONSTRAINT [CK_Refunds_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [FK_RefundRequests_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefundRequests_Users_ClaimedBy] FOREIGN KEY ([ClaimedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefundRequests_Users_CompletedBy] FOREIGN KEY ([CompletedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefundRequests_Users_DecidedBy] FOREIGN KEY ([DecidedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefundRequests_Users_RequestedBy] FOREIGN KEY ([RequestedBy]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);


CREATE INDEX [IX_MenuItems_CategoryId] ON [MenuItems] ([CategoryId]);


CREATE UNIQUE INDEX [IX_Notifications_UserId_EventKey] ON [Notifications] ([UserId], [EventKey]);


CREATE INDEX [IX_Notifications_UserId_ReadAtUtc_Id] ON [Notifications] ([UserId], [ReadAtUtc], [Id]);


CREATE INDEX [IX_OrderItems_MenuItemId] ON [OrderItems] ([MenuItemId]);


CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);


CREATE UNIQUE INDEX [IX_Orders_DiningTableId] ON [Orders] ([DiningTableId]) WHERE [DiningTableId] IS NOT NULL AND [Closed] = 0;


CREATE UNIQUE INDEX [IX_Orders_PosRequestKey] ON [Orders] ([PosRequestKey]) WHERE [PosRequestKey] IS NOT NULL;


CREATE INDEX [IX_Orders_UserId] ON [Orders] ([UserId]);


CREATE UNIQUE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);


CREATE UNIQUE INDEX [IX_Promotions_Code] ON [Promotions] ([Code]);


CREATE INDEX [IX_Reconciliations_BusinessDate] ON [Reconciliations] ([BusinessDate]);


CREATE INDEX [IX_Reconciliations_RecordedBy] ON [Reconciliations] ([RecordedBy]);


CREATE UNIQUE INDEX [IX_RefundRequests_ActivePaymentId] ON [RefundRequests] ([ActivePaymentId]) WHERE [ActivePaymentId] IS NOT NULL;


CREATE INDEX [IX_RefundRequests_ClaimedBy] ON [RefundRequests] ([ClaimedBy]);


CREATE INDEX [IX_RefundRequests_CompletedBy] ON [RefundRequests] ([CompletedBy]);


CREATE INDEX [IX_RefundRequests_DecidedBy] ON [RefundRequests] ([DecidedBy]);


CREATE INDEX [IX_RefundRequests_PaymentId] ON [RefundRequests] ([PaymentId]);


CREATE INDEX [IX_RefundRequests_RefundedAtUtc] ON [RefundRequests] ([RefundedAtUtc]);


CREATE UNIQUE INDEX [IX_RefundRequests_RequestedBy_RequestKey] ON [RefundRequests] ([RequestedBy], [RequestKey]);


CREATE INDEX [IX_Reservations_DiningTableId_StartsAt] ON [Reservations] ([DiningTableId], [StartsAt]);


CREATE INDEX [IX_Reservations_UserId] ON [Reservations] ([UserId]);


CREATE INDEX [IX_Reviews_MenuItemId] ON [Reviews] ([MenuItemId]);


CREATE UNIQUE INDEX [IX_Reviews_OrderId_MenuItemId] ON [Reviews] ([OrderId], [MenuItemId]);


CREATE INDEX [IX_Reviews_UserId] ON [Reviews] ([UserId]);


CREATE UNIQUE INDEX [IX_TaxInvoices_Number] ON [TaxInvoices] ([Number]);


CREATE UNIQUE INDEX [IX_TaxInvoices_OrderId] ON [TaxInvoices] ([OrderId]);


CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);



SET IDENTITY_INSERT [Users] ON;
INSERT INTO [Users] ([Id],[Name],[Email],[PasswordHash],[Role],[Phone],[Active]) VALUES (1,N'Admin',N'admin@admin.com',N'AQAAAAIAAYagAAAAEKjdHcEG+URNYg2ZX8izQ8Wtu6D5LP3yl3LllW5bz+ZDaOThlB2AwyuT0rxq8ptHtQ==',N'Admin',N'0812345678',1);
INSERT INTO [Users] ([Id],[Name],[Email],[PasswordHash],[Role],[Phone],[Active]) VALUES (2,N'Manager',N'manager@manager.com',N'AQAAAAIAAYagAAAAEPQVLoxdQS3SKoeLxJ/EoQ1Z4BseCrqQS0PH1/huDtPh/KoleOP2P/A8S1DssZnNaA==',N'Manager',N'0812345678',1);
INSERT INTO [Users] ([Id],[Name],[Email],[PasswordHash],[Role],[Phone],[Active]) VALUES (3,N'Staff',N'staff@staff.com',N'AQAAAAIAAYagAAAAEMUde6ptFODaCK2SymDEiBnnNXEUNs/yngOzXfb3AYy4NMBeP0bgM8Efm9Vlr/Ly3g==',N'Staff',N'0812345678',1);
INSERT INTO [Users] ([Id],[Name],[Email],[PasswordHash],[Role],[Phone],[Active]) VALUES (4,N'คุณอิ่มใจ',N'customer@customer.com',N'AQAAAAIAAYagAAAAEL0tiJB52yOQlW5G8aDF0lyJk+36sPvWKbQ40+ZGjnGDC9yj4Nugog8yE5RS+BB1Hg==',N'Customer',N'0812345678',1);
INSERT INTO [Users] ([Id],[Name],[Email],[PasswordHash],[Role],[Phone],[Active]) VALUES (5,N'นักบัญชี',N'accountant@accountant.com',N'AQAAAAIAAYagAAAAEPwwVdc7gTmLLol3oekIbyvRXxZdloySf8xBSZ/suAy8W7WNlDz2iXSpUtxvYU7mzw==',N'Accountant',N'0812345678',1);
SET IDENTITY_INSERT [Users] OFF;
SET IDENTITY_INSERT [Categories] ON;
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (1,N'ข้าว',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (2,N'เส้นและซุป',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (3,N'สลัดและผัก',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (4,N'สเต๊ก',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (5,N'พิซซ่าและเบอร์เกอร์',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (6,N'ของหวาน',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (7,N'เครื่องดื่ม',1);
INSERT INTO [Categories] ([Id],[Name],[Active]) VALUES (8,N'กาแฟ',1);
SET IDENTITY_INSERT [Categories] OFF;
SET IDENTITY_INSERT [MenuItems] ON;
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (1,N'ข้าวผัดสูตรเอาอีก',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',89.0,10.0,1,N'/images/food-01.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (2,N'ซุปเส้นน้ำใส',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',109.0,0.0,2,N'/images/food-02.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (3,N'สลัดผักสด',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',119.0,0.0,3,N'/images/food-03.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (4,N'สเต๊กย่างหอมสมุนไพร',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',259.0,0.0,4,N'/images/food-04.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (5,N'พิซซ่าโฮมเมด',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',229.0,0.0,5,N'/images/food-05.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (6,N'เค้กประจำวัน',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',95.0,0.0,6,N'/images/food-06.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (7,N'สลัดโบวล์',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',149.0,15.0,3,N'/images/food-07.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (8,N'เครื่องดื่มเย็นสดชื่น',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',65.0,0.0,7,N'/images/food-08.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (9,N'ผักรวมตามฤดูกาล',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',99.0,0.0,3,N'/images/food-09.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (10,N'เบอร์เกอร์เนื้อย่าง',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',179.0,0.0,5,N'/images/food-10.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (11,N'พาสต้าเส้นสด',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',189.0,10.0,2,N'/images/food-11.jpg',1,0);
INSERT INTO [MenuItems] ([Id],[Name],[Description],[Price],[DiscountPercent],[CategoryId],[ImageUrl],[Active],[SoldOut]) VALUES (12,N'กาแฟคั่วหอม',N'ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้',70.0,0.0,8,N'/images/food-12.jpg',1,0);
SET IDENTITY_INSERT [MenuItems] OFF;
SET IDENTITY_INSERT [DiningTables] ON;
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (1,N'T01',2,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (2,N'T02',2,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (3,N'T03',2,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (4,N'T04',2,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (5,N'T05',4,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (6,N'T06',4,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (7,N'T07',6,1);
INSERT INTO [DiningTables] ([Id],[Name],[Capacity],[Active]) VALUES (8,N'T08',6,1);
SET IDENTITY_INSERT [DiningTables] OFF;
SET IDENTITY_INSERT [Promotions] ON;
INSERT INTO [Promotions] ([Id],[Code],[Name],[Percentage],[Value],[Minimum],[StartsAt],[EndsAt],[Active]) VALUES (1,N'IMDEE10',N'เอาอีก ลด 10%',1,10.0,200.0,DATEADD(year,-1,CONVERT(date,GETDATE())),DATEADD(year,1,CONVERT(date,GETDATE())),1);
SET IDENTITY_INSERT [Promotions] OFF;
SET IDENTITY_INSERT [Settings] ON;
INSERT INTO [Settings] ([Id],[TaxLegalName],[TaxAddress],[TaxId],[TaxBranch],[Name],[Phone],[Address]) VALUES (1,N'ร้านเอาอีก (ONE MORE BITE)',N'99 ถนนพหลโยธิน แขวงเสนานิคม เขตจตุจักร กรุงเทพมหานคร 10900',N'0000000000000',N'00000',N'เอาอีก',N'02-123-4567',N'กรุงเทพมหานคร');
SET IDENTITY_INSERT [Settings] OFF;
INSERT INTO [AppUpdates] ([Name]) VALUES (N'Accountant20261120');
INSERT INTO [AppUpdates] ([Name]) VALUES (N'DemoCredentials20261114');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
