IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE TABLE [Kitaplar] (
        [Id] uniqueidentifier NOT NULL,
        [Ad] nvarchar(150) NOT NULL,
        [Durum] nvarchar(20) NOT NULL,
        [Asama] nvarchar(20) NULL,
        [IlerlemeYuzdesi] tinyint NOT NULL DEFAULT CAST(0 AS tinyint),
        [HataKodu] nvarchar(50) NULL,
        [HataMesaji] nvarchar(500) NULL,
        [PdfDepolamaAnahtari] nvarchar(260) NULL,
        [PdfBoyutuBayt] bigint NULL,
        [SayfaSayisi] int NULL,
        [OlusturulmaZamani] datetime2 NOT NULL DEFAULT (sysutcdatetime()),
        [IslemBaslangicZamani] datetime2 NULL,
        [IslemBitisZamani] datetime2 NULL,
        [SatirVersiyonu] rowversion NOT NULL,
        CONSTRAINT [PK_Kitaplar] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Kitaplar_Basarisiz_Mesaj] CHECK ([Durum] <> 'Failed' OR [HataMesaji] IS NOT NULL),
        CONSTRAINT [CK_Kitaplar_Durum] CHECK ([Durum] IN ('Uploaded', 'Queued', 'Processing', 'Completed', 'Failed')),
        CONSTRAINT [CK_Kitaplar_IlerlemeYuzdesi] CHECK ([IlerlemeYuzdesi] BETWEEN 0 AND 100),
        CONSTRAINT [CK_Kitaplar_Tamamlandi_Pdf] CHECK ([Durum] <> 'Completed' OR [PdfDepolamaAnahtari] IS NOT NULL)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE TABLE [Bildiriler] (
        [Id] uniqueidentifier NOT NULL,
        [KitapId] uniqueidentifier NOT NULL,
        [SiraNo] int NOT NULL,
        [OrijinalDosyaAdi] nvarchar(255) NOT NULL,
        [DepolamaAnahtari] nvarchar(260) NOT NULL,
        [DosyaBoyutuBayt] bigint NOT NULL,
        [Sha256] binary(32) NOT NULL,
        [Baslik] nvarchar(500) NOT NULL,
        [BaslikKaynagi] nvarchar(20) NOT NULL,
        [BaslangicSayfasi] int NULL,
        [BitisSayfasi] int NULL,
        [SilinenEpostaSayisi] int NOT NULL DEFAULT 0,
        [SilinenTelefonSayisi] int NOT NULL DEFAULT 0,
        [YuklenmeZamani] datetime2 NOT NULL,
        CONSTRAINT [PK_Bildiriler] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Bildiriler_SiraNo] CHECK ([SiraNo] >= 1),
        CONSTRAINT [FK_Bildiriler_Kitaplar_KitapId] FOREIGN KEY ([KitapId]) REFERENCES [Kitaplar] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Bildiriler_KitapId_Sha256] ON [Bildiriler] ([KitapId], [Sha256]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Bildiriler_KitapId_SiraNo] ON [Bildiriler] ([KitapId], [SiraNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Kitaplar_Durum] ON [Kitaplar] ([Durum]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Kitaplar_OlusturulmaZamani] ON [Kitaplar] ([OlusturulmaZamani] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925190632_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925190632_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

