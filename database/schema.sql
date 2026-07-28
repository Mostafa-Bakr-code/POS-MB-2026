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

CREATE TABLE dbo.Items
(
    ItemId    INT IDENTITY(1,1) NOT NULL,
    ItemName  NVARCHAR(50)      NOT NULL,
    CategoryId INT              NOT NULL,
    Price     DECIMAL(18,4)     NOT NULL,
    TaxRate   DECIMAL(18,4)     NOT NULL CONSTRAINT DF_Items_TaxRate DEFAULT (14.00),
    IsActive  BIT               NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT (1),
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
    UserId          INT               NULL, -- staff cashier; NULL for mobile orders (no student/customer entity yet)
    OrderSource     TINYINT           NOT NULL CONSTRAINT DF_Orders_OrderSource DEFAULT (0), -- 0=Cashier,1=Mobile
    Status          TINYINT           NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0), -- 0=Placed,1=Preparing,2=Ready,3=Completed,4=Cancelled
    IsComplimentary BIT               NOT NULL CONSTRAINT DF_Orders_IsComplimentary DEFAULT (0),
    CreatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_Orders_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId),
    CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId)
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
    Comment         NVARCHAR(50)      NULL,
    CreatedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_OrderItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_OrderItems PRIMARY KEY CLUSTERED (OrderItemId),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId),
    CONSTRAINT FK_OrderItems_Items FOREIGN KEY (ItemId) REFERENCES dbo.Items (ItemId)
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
