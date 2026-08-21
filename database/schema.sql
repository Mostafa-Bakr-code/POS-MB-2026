-- POS-MB database schema
-- Rewrite of the old Dimash-Street POS database, redesigned per the schema-review discussion:
--   see project memory "project-pos-mb-rewrite" for the full list of changes vs the old DB.

CREATE DATABASE [POS-MB];
GO

USE [POS-MB];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.Categories
(
    CategoryId   INT IDENTITY(1,1) NOT NULL,
    CategoryName NVARCHAR(20)      NOT NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT (1),
    CreatedAt    DATETIME2(3)      NOT NULL CONSTRAINT DF_Categories_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt    DATETIME2(3)      NOT NULL CONSTRAINT DF_Categories_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (CategoryId)
);
GO

CREATE TABLE dbo.Users
(
    UserId    INT IDENTITY(1,1) NOT NULL,
    UserName  NVARCHAR(50)      NOT NULL,
    Password  NVARCHAR(200)     NOT NULL,
    Permissions INT             NOT NULL,
    IsActive  BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    CreatedAt DATETIME2(3)      NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3)      NOT NULL CONSTRAINT DF_Users_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (UserId),
    CONSTRAINT UQ_Users_UserName UNIQUE (UserName)
);
GO

-- Separate from Users on purpose: students self-register with an email/password,
-- have no Permissions bitmask (they can only ever place/view their own orders),
-- and are never staff. Email verification was deliberately skipped for v1 (can be
-- added later as a purely additive change - see project memory) - IsActive still
-- exists for an admin to disable an account if needed.
CREATE TABLE dbo.Students
(
    StudentId    INT IDENTITY(1,1) NOT NULL,
    Email        NVARCHAR(256)     NOT NULL,
    Password     NVARCHAR(200)     NOT NULL,
    -- One saved card at a time, not a multi-card wallet - all three are set
    -- together (from a Paymob card-token webhook) or all left NULL. Token is
    -- the opaque string needed to charge the card again; MaskedPan/Subtype
    -- are display-only ("Card ending in 2346 (MasterCard)") - never a real
    -- card number, Paymob never sends us one.
    SavedCardToken     NVARCHAR(255) NULL,
    SavedCardMaskedPan NVARCHAR(32)  NULL,
    SavedCardSubtype   NVARCHAR(32)  NULL,
    -- Forgot-password flow: a 6-digit code, hashed (never stored plaintext,
    -- same reasoning as the password itself) with a short expiration - see
    -- clsStudentBusiness.RequestPasswordResetAsync/ResetPasswordAsync. Both
    -- NULL whenever there's no reset in progress.
    PasswordResetCodeHash      NVARCHAR(255) NULL,
    PasswordResetCodeExpiresAt DATETIME2     NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Students_IsActive DEFAULT (1),
    CreatedAt    DATETIME2(3)      NOT NULL CONSTRAINT DF_Students_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt    DATETIME2(3)      NOT NULL CONSTRAINT DF_Students_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Students PRIMARY KEY CLUSTERED (StudentId),
    CONSTRAINT UQ_Students_Email UNIQUE (Email)
);
GO

CREATE TABLE dbo.Items
(
    ItemId    INT IDENTITY(1,1) NOT NULL,
    ItemName  NVARCHAR(50)      NOT NULL,
    CategoryId INT              NOT NULL,
    Price     DECIMAL(18,4)     NOT NULL,
    TaxRate   DECIMAL(18,4)     NOT NULL CONSTRAINT DF_Items_TaxRate DEFAULT (14.00),
    IsActive  BIT               NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT (1), -- permanent retirement (admin action)
    IsAvailable BIT             NOT NULL CONSTRAINT DF_Items_IsAvailable DEFAULT (1), -- temporary "out of stock right now" toggle, independent of IsActive
    CreatedAt DATETIME2(3)      NOT NULL CONSTRAINT DF_Items_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3)      NOT NULL CONSTRAINT DF_Items_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Items PRIMARY KEY CLUSTERED (ItemId),
    CONSTRAINT FK_Items_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories (CategoryId)
);
GO

CREATE TABLE dbo.Orders
(
    OrderId         INT IDENTITY(1,1) NOT NULL,
    Date            DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_Date DEFAULT (SYSUTCDATETIME()),
    Total           DECIMAL(18,4)     NOT NULL CONSTRAINT DF_Orders_Total DEFAULT (0),
    SerialNumber    INT               NULL,
    OrderDate AS (CAST(Date AS DATE)) PERSISTED,
    UserId          INT               NULL, -- staff cashier; set for Cashier orders, always NULL for Mobile orders
    StudentId       INT               NULL, -- student who placed it; set for Mobile orders, always NULL for Cashier orders
    OrderSource     TINYINT           NOT NULL CONSTRAINT DF_Orders_OrderSource DEFAULT (0), -- 0=Cashier,1=Mobile
    Status          TINYINT           NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0), -- 0=Placed,1=Preparing,2=Ready,3=Completed,4=Cancelled
    IsComplimentary BIT               NOT NULL CONSTRAINT DF_Orders_IsComplimentary DEFAULT (0),
    KitchenTicketPrintedAt DATETIME2  NULL, -- set once the cashier PC's poller has printed this order's kitchen ticket; NULL means "still needs printing" (only meaningful for Mobile orders in Preparing)
    PaymobTransactionId BIGINT        NULL, -- set once Paymob confirms payment (the webhook's obj.id); needed to tell Paymob which transaction to refund if the order is later cancelled
    RefundedAt      DATETIME2         NULL, -- set once an automatic refund succeeds; guards against ever double-refunding the same order
    RefundTransactionId BIGINT        NULL, -- Paymob's own transaction id for the refund itself - a separate record from PaymobTransactionId (the original charge), linked to it via Paymob's own parent_transaction field
    PaymobReferences NVARCHAR(500) NULL, -- semicolon-separated special_reference of EVERY Paymob checkout attempt for this order (initial + every retry), not just the latest - a student backing out mid-attempt and retrying must never make an earlier, still-resolving attempt permanently unverifiable. Lets ResumeCheckoutAsync/the auto-cancel sweep/the order-detail poll ask Paymob directly "did any of these already succeed?"
    CancelledBy     NVARCHAR(100)     NULL, -- who/what cancelled this order - "Student", "Staff: {username}", "Auto (kitchen didn't accept in time)", "Auto (payment abandoned)"; NULL for anything never cancelled
    CreatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId),
    CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Orders_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students (StudentId)
);
GO

CREATE TABLE dbo.OrderItems
(
    OrderItemId     INT IDENTITY(1,1) NOT NULL,
    OrderId         INT               NOT NULL,
    ItemId          INT               NOT NULL,
    Quantity        INT               NOT NULL,
    Price           DECIMAL(18,4)     NOT NULL,
    TotalItemsPrice DECIMAL(18,4)     NOT NULL,
    TaxRate         DECIMAL(18,4)     NOT NULL CONSTRAINT DF_OrderItems_TaxRate DEFAULT (14.00), -- snapshot of Item.TaxRate at time of sale, for accurate historical tax reporting
    Comment         NVARCHAR(50)      NULL,
    CreatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_OrderItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_OrderItems PRIMARY KEY CLUSTERED (OrderItemId),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId),
    CONSTRAINT FK_OrderItems_Items FOREIGN KEY (ItemId) REFERENCES dbo.Items (ItemId)
);
GO

CREATE TABLE dbo.ItemPriceHistory
(
    ItemPriceHistoryId INT IDENTITY(1,1) NOT NULL,
    ItemId          INT           NOT NULL,
    OldPrice        DECIMAL(18,4) NOT NULL,
    NewPrice        DECIMAL(18,4) NOT NULL,
    OldTaxRate      DECIMAL(18,4) NOT NULL,
    NewTaxRate      DECIMAL(18,4) NOT NULL,
    ChangedByUserId INT           NULL, -- who made the change; client-asserted (no server-side auth yet, see project memory)
    ChangedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_ItemPriceHistory_ChangedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ItemPriceHistory PRIMARY KEY CLUSTERED (ItemPriceHistoryId),
    CONSTRAINT FK_ItemPriceHistory_Items FOREIGN KEY (ItemId) REFERENCES dbo.Items (ItemId),
    CONSTRAINT FK_ItemPriceHistory_Users FOREIGN KEY (ChangedByUserId) REFERENCES dbo.Users (UserId)
);
GO

CREATE TABLE dbo.Logs
(
    LogId  INT IDENTITY(1,1) NOT NULL,
    UserId INT               NOT NULL,
    LogIn  DATETIME2(3)      NOT NULL,
    LogOut DATETIME2(3)      NULL,
    CONSTRAINT PK_Logs PRIMARY KEY CLUSTERED (LogId),
    CONSTRAINT FK_Logs_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId)
);
GO

CREATE TABLE dbo.Settings
(
    Id    INT IDENTITY(1,1) NOT NULL,
    [Key]   NVARCHAR(100)   NOT NULL,
    [Value] NVARCHAR(MAX)   NULL,
    CONSTRAINT PK_Settings PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Settings_Key UNIQUE ([Key])
);
GO

-- Helps enforce/guard SerialNumber uniqueness per day at the DB level as a backstop
-- (generation logic itself is fixed at the business-logic stage, not here).
CREATE UNIQUE INDEX UQ_Orders_Date_SerialNumber
    ON dbo.Orders (OrderDate, SerialNumber)
    WHERE SerialNumber IS NOT NULL;
GO

-- UserId has no FK constraint (deliberately) - it refers to either Users.UserId
-- or Students.StudentId depending on AccountType, since refresh token rotation
-- and theft-detection are identical mechanics regardless of account type and
-- don't need two parallel copies of this table/logic. AccountType is what tells
-- the API which table to actually look the principal up in.
CREATE TABLE dbo.RefreshTokens
(
    RefreshTokenId INT IDENTITY(1,1) NOT NULL,
    UserId         INT               NOT NULL,
    AccountType    TINYINT           NOT NULL CONSTRAINT DF_RefreshTokens_AccountType DEFAULT (0), -- 0=Staff (Users), 1=Student (Students)
    TokenHash      NVARCHAR(200)     NOT NULL, -- SHA-256 of the token; the plaintext itself is never stored, same reasoning as passwords
    ExpiresAt      DATETIME2(3)      NOT NULL,
    RevokedAt      DATETIME2(3)      NULL, -- set on logout, on rotation (a new token replaces it), or on reuse-detected theft response
    RevokedViaLogout BIT             NOT NULL CONSTRAINT DF_RefreshTokens_RevokedViaLogout DEFAULT (0), -- distinguishes an explicit logout from a rotation, so a benign logout-vs-in-flight-refresh race isn't misread as token theft
    CreatedAt      DATETIME2(3)      NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_RefreshTokens PRIMARY KEY CLUSTERED (RefreshTokenId)
);
GO

CREATE INDEX IX_RefreshTokens_TokenHash ON dbo.RefreshTokens (TokenHash);
GO
