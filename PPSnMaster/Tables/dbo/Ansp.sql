﻿CREATE TABLE [dbo].[Ansp]
(
	[Id] BIGINT NOT NULL CONSTRAINT pkAnspId PRIMARY KEY IDENTITY (1,1), 
    [ObjkId] BIGINT NOT NULL CONSTRAINT fkAnspObjkId REFERENCES dbo.Objk (Id), 
    [Name] NVARCHAR(100) NOT NULL, 
    [Titel] NVARCHAR(30) NULL, 
    [Tel] VARCHAR(30) NULL, 
    [Fax] VARCHAR(30) NULL, 
    [Mobil] VARCHAR(30) NULL, 
    [Mail] NVARCHAR(100) NULL, 
    [Std] BIT NOT NULL DEFAULT 0
)

GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'PK',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Id'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'FK zu Objk',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'ObjkId'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Name des Ansprechpartners',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Name'
GO

EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'akademischer Titel',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Titel'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Telefonnummer',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Tel'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Faxnummer',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Fax'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Mobilnummer (Handy)',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Mobil'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Mailadresse',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Mail'
GO
EXEC sp_addextendedproperty @name = N'MS_Description',
    @value = N'Standardansprechpartner',
    @level0type = N'SCHEMA',
    @level0name = N'dbo',
    @level1type = N'TABLE',
    @level1name = N'Ansp',
    @level2type = N'COLUMN',
    @level2name = N'Std'